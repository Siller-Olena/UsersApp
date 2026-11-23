using System.Collections.Concurrent;
using System.Net;
using System.Text.Json;
using System.Threading.Channels;
using k8s;
using k8s.Autorest;
using k8s.Models;

namespace UsersApp.BackupController;

// Контролер: спостерігає за DatabaseBackup і підтримує для кожного CronJob з тим самим іменем.
public class BackupWorker : BackgroundService
{
    private const string Group = "backup.example.com";
    private const string Version = "v1alpha1";
    private const string Plural = "databasebackups";
    private const string OwnerApiVersion = Group + "/" + Version;
    private const string OwnerKind = "DatabaseBackup";
    private const string ContainerImage = "postgres:16-alpine"; // клієнт сумісний із сервером 16

    private readonly ILogger<BackupWorker> _log;
    private readonly string _ns = Environment.GetEnvironmentVariable("WATCH_NAMESPACE") ?? "demo";

    // черга звірки + множина "вже в черзі": однакові події не створюють дублікатів
    private readonly Channel<string> _queue = Channel.CreateUnbounded<string>();
    private readonly ConcurrentDictionary<string, byte> _pending = new();

    private Kubernetes _api = default!;

    public BackupWorker(ILogger<BackupWorker> log) => _log = log;

    protected override async Task ExecuteAsync(CancellationToken ct)
    {
        _api = new Kubernetes(KubernetesClientConfiguration.InClusterConfig());
        _log.LogInformation("Контролер запущено, namespace={Ns}", _ns);

        await Task.WhenAll(
            WatchLoopAsync(ct),     // зміни DatabaseBackup
            ResyncLoopAsync(ct),    // періодична повна звірка (відновлює видалений CronJob)
            ReconcileLoopAsync(ct)  // обробка черги
        );
    }

    private void Enqueue(string name)
    {
        if (_pending.TryAdd(name, 0))
            _queue.Writer.TryWrite(name);
    }

    // ---------- 1. LIST + WATCH ----------
    private async Task WatchLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await WatchOnceAsync(ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
            catch (Exception ex) when (ex is not HttpOperationException)
            {
                // сеанс watch закінчився (таймаут 30 с, обрив потоку): не помилка API, просто відновлюємо спостереження
                _log.LogInformation("Сеанс watch завершено ({Type}), відновлюю спостереження", ex.GetType().Name);
                await Task.Delay(TimeSpan.FromSeconds(1), ct);
            }
            catch (Exception ex)
            {
                // відповідь API з помилкою (наприклад, 410 Gone): наступна ітерація знову починається з LIST
                _log.LogWarning("Watch перервано ({Msg}), повторюю list + watch", ex.Message);
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
        }
    }

    private async Task WatchOnceAsync(CancellationToken ct)
    {
        var custom = _api.CustomObjects;

        using var snapshot = await custom
            .ListNamespacedCustomObjectWithHttpMessagesAsync<JsonElement>(
                Group, Version, _ns, Plural, cancellationToken: ct);

        foreach (var item in snapshot.Body.GetProperty("items").EnumerateArray())
            Enqueue(item.GetProperty("metadata").GetProperty("name").GetString()!);

        var rv = snapshot.Body.GetProperty("metadata").GetProperty("resourceVersion").GetString();

        var request = custom.ListNamespacedCustomObjectWithHttpMessagesAsync<JsonElement>(
            Group, Version, _ns, Plural, resourceVersion: rv,
            timeoutSeconds: 30, watch: true, cancellationToken: ct);

        var changes = request.WatchAsync<JsonElement, JsonElement>(
            error => _log.LogWarning("Watch error: {Msg}", error.Message), ct);

        await foreach (var (type, item) in changes.WithCancellation(ct))
        {
            if (type is WatchEventType.Added or WatchEventType.Modified or WatchEventType.Deleted)
            {
                var name = item.GetProperty("metadata").GetProperty("name").GetString()!;
                _log.LogInformation("Подія {Type} demo/{Name}", type, name);
                Enqueue(name);
            }
        }
        // потік закінчився (timeout 30 с): цикл відновить спостереження
    }

    // ---------- 2. Періодична повна звірка ----------
    private async Task ResyncLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromSeconds(20), ct);
            try
            {
                using var list = await _api.CustomObjects
                    .ListNamespacedCustomObjectWithHttpMessagesAsync<JsonElement>(
                        Group, Version, _ns, Plural, cancellationToken: ct);
                foreach (var item in list.Body.GetProperty("items").EnumerateArray())
                    Enqueue(item.GetProperty("metadata").GetProperty("name").GetString()!);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogWarning("Resync: {Msg}", ex.Message);
            }
        }
    }

    // ---------- 3. Обробка черги ----------
    private async Task ReconcileLoopAsync(CancellationToken ct)
    {
        await foreach (var name in _queue.Reader.ReadAllAsync(ct))
        {
            _pending.TryRemove(name, out _);   // дозволяє нові події для цього імені
            try
            {
                await ReconcileAsync(name, ct);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError("Звірка {Name} не вдалася: {Msg}", name, ex.Message);
            }
        }
    }

    private async Task ReconcileAsync(string name, CancellationToken ct)
    {
        // прочитати поточний DatabaseBackup (list з fieldSelector по імені)
        using var found = await _api.CustomObjects
            .ListNamespacedCustomObjectWithHttpMessagesAsync<JsonElement>(
                Group, Version, _ns, Plural, fieldSelector: $"metadata.name={name}",
                cancellationToken: ct);

        var items = found.Body.GetProperty("items");
        if (items.GetArrayLength() == 0)
        {
            // DatabaseBackup видалено: CronJob прибере garbage collector через ownerReferences
            _log.LogInformation("DatabaseBackup {Name} відсутній, нічого робити", name);
            return;
        }

        var backup = items[0];
        var uid = backup.GetProperty("metadata").GetProperty("uid").GetString()!;
        var spec = backup.GetProperty("spec");
        var schedule = spec.GetProperty("schedule").GetString()!;
        var timeZone = spec.TryGetProperty("timeZone", out var tz) ? tz.GetString()! : "Etc/UTC";
        var secretName = spec.GetProperty("connectionSecretName").GetString()!;
        var pvcName = spec.GetProperty("backupPVCName").GetString()!;

        var desired = BuildCronJob(name, uid, schedule, timeZone, secretName, pvcName);

        V1CronJob? existing = null;
        try
        {
            existing = await _api.BatchV1.ReadNamespacedCronJobAsync(name, _ns, cancellationToken: ct);
        }
        catch (HttpOperationException e) when (e.Response.StatusCode == HttpStatusCode.NotFound) { }

        if (existing is null)
        {
            await _api.BatchV1.CreateNamespacedCronJobAsync(desired, _ns, cancellationToken: ct);
            _log.LogInformation("Створено CronJob {Name}", name);
            return;
        }

        // чужий CronJob з таким самим іменем: не чіпаємо
        var ownedByUs = existing.Metadata.OwnerReferences?.Any(o => o.Uid == uid) ?? false;
        if (!ownedByUs)
        {
            _log.LogError("Конфлікт: CronJob {Name} уже існує і не належить DatabaseBackup {Uid}", name, uid);
            return;
        }

        if (NeedsUpdate(existing, schedule, timeZone, secretName, pvcName))
        {
            desired.Metadata.ResourceVersion = existing.Metadata.ResourceVersion;
            await _api.BatchV1.ReplaceNamespacedCronJobAsync(desired, name, _ns, cancellationToken: ct);
            _log.LogInformation("Оновлено CronJob {Name} (schedule={Schedule})", name, schedule);
        }
    }

    // порівнюємо лише керовані поля (щоб значення за замовчуванням від API не давали вічних оновлень)
    private static bool NeedsUpdate(V1CronJob cj, string schedule, string tz, string secret, string pvc)
    {
        var pod = cj.Spec.JobTemplate.Spec.Template.Spec;
        var container = pod.Containers.FirstOrDefault();

        if (cj.Spec.Schedule != schedule) return true;
        if (cj.Spec.TimeZone != tz) return true;
        if (cj.Spec.ConcurrencyPolicy != "Forbid") return true;
        if (container?.EnvFrom?.FirstOrDefault()?.SecretRef?.Name != secret) return true;
        if (pod.Volumes?.FirstOrDefault()?.PersistentVolumeClaim?.ClaimName != pvc) return true;
        return false;
    }

    // ---------- Шаблон CronJob ----------
    private V1CronJob BuildCronJob(string name, string uid, string schedule, string tz,
                                   string secretName, string pvcName)
    {
        // pg_dump у тимчасовий файл, після успіху перейменування на унікальний .dump
        const string script = """
            set -e
            STAMP=$(date -u +%Y%m%d-%H%M%S)
            TMP="/backups/.${BACKUP_NAME}-${STAMP}-$$.tmp"
            FINAL="/backups/${BACKUP_NAME}-${STAMP}-$$.dump"
            if ! pg_dump -Fc -f "$TMP"; then
              rm -f "$TMP"
              echo "pg_dump failed" >&2
              exit 1
            fi
            mv "$TMP" "$FINAL"
            echo "Backup ready: $FINAL"
            """;

        return new V1CronJob
        {
            ApiVersion = "batch/v1",
            Kind = "CronJob",
            Metadata = new V1ObjectMeta
            {
                Name = name,
                NamespaceProperty = _ns,
                OwnerReferences = new List<V1OwnerReference>
                {
                    new()
                    {
                        ApiVersion = OwnerApiVersion,
                        Kind = OwnerKind,
                        Name = name,
                        Uid = uid,
                        Controller = true
                    }
                }
            },
            Spec = new V1CronJobSpec
            {
                Schedule = schedule,
                TimeZone = tz,
                ConcurrencyPolicy = "Forbid",
                SuccessfulJobsHistoryLimit = 3,
                FailedJobsHistoryLimit = 3,
                JobTemplate = new V1JobTemplateSpec
                {
                    Spec = new V1JobSpec
                    {
                        BackoffLimit = 2,
                        Template = new V1PodTemplateSpec
                        {
                            Spec = new V1PodSpec
                            {
                                RestartPolicy = "Never",
                                Containers = new List<V1Container>
                                {
                                    new()
                                    {
                                        Name = "pg-dump",
                                        Image = ContainerImage,
                                        Command = new List<string> { "sh", "-c", script },
                                        Env = new List<V1EnvVar>
                                        {
                                            new() { Name = "BACKUP_NAME", Value = name }
                                        },
                                        EnvFrom = new List<V1EnvFromSource>
                                        {
                                            new() { SecretRef = new V1SecretEnvSource { Name = secretName } }
                                        },
                                        VolumeMounts = new List<V1VolumeMount>
                                        {
                                            new() { Name = "backups", MountPath = "/backups" }
                                        }
                                    }
                                },
                                Volumes = new List<V1Volume>
                                {
                                    new()
                                    {
                                        Name = "backups",
                                        PersistentVolumeClaim = new V1PersistentVolumeClaimVolumeSource
                                        {
                                            ClaimName = pvcName
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        };
    }
}
