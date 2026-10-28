using Akka.Actor;
using Akka.Hosting;
using Akka.Serialization;
using Demo.Core.Chat;
using Demo.Core.Users;

namespace Demo.Core;

[AkkaSerializer<IWithUserId>("user-serializer", 120001)] // covers all chat messages too
[AkkaSerializable<ChatResponse>(Manifest = "other-msg")] // add a non-IWithUserId message to the serializer
public sealed partial class ChatSerializer : AkkaSerializer
{
    // Used for Akka.Hosting registration
    public static partial SerializerDetails CreateSerializerDetails(ExtendedActorSystem system);
}

public static class AkkaHostingExtensionMethods
{
    public static AkkaConfigurationBuilder WithChatSerializer(this AkkaConfigurationBuilder builder)
        => builder.WithCustomSerializer(ChatSerializer.CreateSerializerDetails);
}