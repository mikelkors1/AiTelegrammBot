using ItmoBot.Configuration.Models;
using ItmoBot.Hosting.Composition.Registrations;

namespace ItmoBot.Hosting.Composition;

public sealed class ApplicationComposition(Settings settings)
{
    public void Configure(HostApplicationBuilder builder)
    {
        builder.Services.AddSingleton(settings);
        new LoggingRegistration(settings).Register(builder.Logging, builder.Services);
        new InfrastructureRegistration(settings).Register(builder.Services);
        new ApplicationRegistration().Register(builder.Services);
        new ContextRegistration(settings.Llm).Register(builder.Services);
        new DialogueRegistration(settings.Llm).Register(builder.Services);
        new HostingRegistration().Register(builder.Services);
        ConfigureContainerValidation(builder);
    }

    private void ConfigureContainerValidation(HostApplicationBuilder builder)
    {
        builder.ConfigureContainer(new DefaultServiceProviderFactory(new ServiceProviderOptions
        {
            ValidateScopes = true,
            ValidateOnBuild = true,
        }));
    }
}
