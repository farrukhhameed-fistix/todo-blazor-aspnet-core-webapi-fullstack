var builder = DistributedApplication.CreateBuilder(args);

// pgvector image required — EF migrations use the Postgres vector extension.
var postgres = builder
    .AddPostgres("postgres")
    .WithImage("pgvector/pgvector")
    .WithImageTag("pg16")
    .WithDataVolume("taskmanager-aspire-pgdata")
    .WithPgAdmin(pgAdmin => pgAdmin.WithHostPort(5050));

// Resource name "MainDb" injects ConnectionStrings__MainDb for WebApi MasterConfig binding.
var mainDb = postgres.AddDatabase("MainDb", databaseName: "taskdb");

// Local OpenAI-compatible STT (Speaches / faster-whisper). CPU image + tiny model for laptop demos.
// Persist HF model cache so Aspire/container restarts do not re-download every time.
var whisper = builder
    .AddContainer("whisper", "ghcr.io/speaches-ai/speaches", "latest-cpu")
    .WithHttpEndpoint(targetPort: 8000, name: "http")
    .WithEnvironment("WHISPER__MODEL", "Systran/faster-whisper-tiny")
    .WithVolume("taskmanager-aspire-whisper-cache", "/home/ubuntu/.cache/huggingface/hub");

// AddProject already registers http/https endpoints from the project; update ports
// instead of calling WithHttp(s)Endpoint (those try to create duplicate names on Aspire 13.1).
var webapi = builder
    .AddProject<Projects.WebApi>("webapi")
    .WithReference(mainDb)
    .WaitFor(mainDb)
    .WaitFor(whisper)
    .WithEnvironment("Ai__SpeechToText__Endpoint", whisper.GetEndpoint("http"))
    .WithEndpoint("http", endpoint =>
    {
        endpoint.Port = 5000;
        endpoint.IsExternal = true;
    })
    .WithEndpoint("https", endpoint =>
    {
        endpoint.Port = 5001;
        endpoint.IsExternal = true;
    });

builder
    .AddProject<Projects.WebApp>("webapp")
    .WithEndpoint("https", endpoint =>
    {
        endpoint.Port = 5002;
        endpoint.IsExternal = true;
    })
    .WaitFor(webapi);

builder.Build().Run();
