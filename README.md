# UsersApp

Розподілене рішення для керування користувачами: односторінковий клієнт (nginx), Web API (ASP.NET Core, EF Core Code-First), PostgreSQL, окремий застосунок міграцій (job) і Helm Chart для Kubernetes.
У проєкт також входить лабораторна № 5: власний ресурс `DatabaseBackup` і контролер, що створює CronJob для резервного копіювання PostgreSQL.

## Структура

| Папка | Вміст |
|---|---|
| `src/UsersApp.Data` | модель, `AppDbContext`, міграції EF Core |
| `src/UsersApp.Api` | Web API (контролер `UsersController`, Swagger) |
| `src/UsersApp.Migrator` | консольний застосунок, що застосовує міграції |
| `src/UsersApp.BackupController` | контролер `DatabaseBackup` (лабораторна 5) |
| `web` | клієнт (HTML/JS) і Dockerfile з nginx |
| `k8s/usersapp` | Helm Chart, `k8s/values.lab.yaml`: налаштування середовища |
| `k8s-backup` | маніфести лабораторної 5 (CRD, RBAC, Deployment тощо) |

## Локальний запуск

```bash
docker run --rm -d --name usersapp-db -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=usersapp -p 5432:5432 postgres:16-alpine
```
PowerShell:
```powershell
$env:ConnectionStrings__Default = "Host=localhost;Port=5432;Database=usersapp;Username=postgres;Password=postgres"
dotnet run --project src/UsersApp.Migrator
dotnet run --project src/UsersApp.Api --urls http://localhost:8080
```
Swagger: http://localhost:8080/swagger. Клієнт: відкрити `web/index.html` не можна (потрібен API за тим самим хостом), тому використовуйте Docker або Kubernetes (нижче).

## Міграції (Code-First)

```bash
dotnet tool restore
dotnet dotnet-ef migrations add <Name> --project src/UsersApp.Data --startup-project src/UsersApp.Migrator
```

## Docker-образи

```bash
docker build -t sillerolena/usersapp-api:latest -f src/UsersApp.Api/Dockerfile .
docker build -t sillerolena/usersapp-migrator:latest -f src/UsersApp.Migrator/Dockerfile .
docker build -t sillerolena/usersapp-web:latest web
```
У CI образи збираються та публікуються в Docker Hub (секрети `DOCKERHUB_USERNAME` і `DOCKERHUB_TOKEN`).

## Helm Chart

Перевірка та пакування:
```bash
helm lint k8s/usersapp -f k8s/values.lab.yaml
helm package k8s/usersapp --destination dist
```
Встановлення (потрібен NGINX Ingress Controller):
```bash
helm upgrade --install usersapp k8s/usersapp -n usersapp --create-namespace -f k8s/values.lab.yaml --wait --wait-for-jobs --timeout 5m
kubectl get pods -n usersapp
```
У файл `hosts` додайте `127.0.0.1 usersapp.lab.test`; застосунок відкривається за адресою http://usersapp.lab.test.

## Лабораторна 5: резервне копіювання PostgreSQL

```bash
kubectl apply -f k8s-backup/00-namespace.yaml
kubectl apply -f k8s-backup/postgres.yaml
kubectl apply -f k8s-backup/secret.example.yaml
kubectl apply -f k8s-backup/pvc.yaml
kubectl apply -f k8s-backup/databasebackup-crd.yaml
kubectl wait --for=condition=Established crd/databasebackups.backup.example.com
docker build -t sillerolena/usersapp-backup-controller:0.1 -f src/UsersApp.BackupController/Dockerfile .
kubectl apply -f k8s-backup/rbac.yaml
kubectl apply -f k8s-backup/deployment.yaml
kubectl apply -f k8s-backup/databasebackup.yaml
kubectl get databasebackup,cronjob -n demo
```
Secret із демонстраційними значеннями описано в `k8s-backup/secret.example.yaml`.
