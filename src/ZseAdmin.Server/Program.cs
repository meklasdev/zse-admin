using ZseAdmin.Server;

var app = ServerHost.Build(args);
if (app is null)
{
    return 1;
}

await app.RunAsync();
return 0;
