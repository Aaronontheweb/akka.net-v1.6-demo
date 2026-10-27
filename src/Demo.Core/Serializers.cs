using Demo.Core.Chat;
using Demo.Core.Users;

namespace Demo.Core;

[AkkaSerializer<IWithUserId>("user-serializer", 120001)]
public sealed partial class ChatSerializer : AkkaSerializer
{
    public static partial SerializerRegistration CreateRegistration();
}