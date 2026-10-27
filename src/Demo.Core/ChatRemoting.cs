using System.Diagnostics.CodeAnalysis;
using Akka.Actor;
using Akka.Hosting;
using Akka.Remote;

namespace Demo.Core;

public static class ChatRemoting
{
    public const string ServerPath = "akka://ChatServer@127.0.0.1:8081/user/chat";

    // Remote DeathWatch still loads its failure detector by name in 1.6 beta3.
    [DynamicDependency(DynamicallyAccessedMemberTypes.PublicConstructors, typeof(PhiAccrualFailureDetector))]
    public static AkkaConfigurationBuilder WithChatRemoting(this AkkaConfigurationBuilder builder, int port) =>
        builder.WithActorRefProvider(ProviderSelection.Remote.Instance)
            .AddHocon($$"""
                akka.remote.artery {
                    enabled = on
                    transport = tcp
                    canonical.hostname = "127.0.0.1"
                    canonical.port = {{port}}
                }
                """, HoconAddMode.Prepend)
            .WithChatSerializer();
}
