using Alycia.Api;
using Alycia.DevHost;

AlyciaApiHost.Build(args, DevelopmentInference.AddMocks, DevelopmentInference.MapDemo).Run();
