using UsersApp.BackupController;

var builder = Host.CreateApplicationBuilder(args);
builder.Services.AddHostedService<BackupWorker>();
builder.Build().Run();
