//Main
using raft_backend.Modules;

var builder = WebApplication.CreateBuilder(args);

builder.AddRaftModules();

var app = builder.Build();

app.UseRaftModules();

app.Run();
