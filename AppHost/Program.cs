var builder = DistributedApplication.CreateBuilder(args);

var identityDb = builder.AddPostgres("pg")
                        .WithDataVolume()
                        .AddDatabase("identityDb");

builder.AddProject<Projects.AppWithIdentity>("webapp")
       .WithReference(identityDb);

builder.Build().Run();