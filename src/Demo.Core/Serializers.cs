using Akka.Hosting;
using Demo.Core.Chat;
using Demo.Core.Users;

namespace Demo.Core;

[AkkaSerializer<IWithUserId>("user-serializer", 120001)] // covers all chat messages too
[AkkaSerializable<ChatResponse>(Manifest = "other-msg")] // add a non-IWithUserId message to the serializer
public sealed partial class ChatSerializer : AkkaSerializer
{
    // Used for Akka.Hosting registration
    public static partial SerializerRegistration CreateRegistration();
}

public static class AkkaHostingExtensionMethods
{
    public static AkkaConfigurationBuilder WithChatSerializer(this AkkaConfigurationBuilder builder)
    {
        var registration = ChatSerializer.CreateRegistration();

        return builder.WithCustomSerializer(
            registration.Alias,
            registration.UseFor,
            system => registration.CreateDetails(system).Serializer);
    }
}