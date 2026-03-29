using Microsoft.Extensions.Hosting;
using SyncMetrics.Pipeline.Infrastructure.Configuration;

var builder = Host.CreateApplicationBuilder(args);

// Wire all pipeline services — config binding, HTTP clients, DI registrations
builder.Services.AddPipelineServices(builder.Configuration);

var app = builder.Build();

// TODO: Phase 9 — Resolve PipelineCoordinator and execute pipeline
Console.WriteLine("SyncMetrics Weather Pipeline — Configuration wired (Phase 3).");
Console.WriteLine("Run 'dotnet test' to verify test projects are wired correctly.");
