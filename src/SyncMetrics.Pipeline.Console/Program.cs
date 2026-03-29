using Microsoft.Extensions.Hosting;

var builder = Host.CreateApplicationBuilder(args);

// TODO: Phase 3+ — Register pipeline services via ServiceRegistration.cs
// builder.Services.AddPipelineServices(builder.Configuration);

var app = builder.Build();

// TODO: Phase 9 — Resolve PipelineCoordinator and execute pipeline
Console.WriteLine("SyncMetrics Weather Pipeline — Phase 1 scaffolding complete.");
Console.WriteLine("Run 'dotnet test' to verify test projects are wired correctly.");
