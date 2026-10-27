using System.Runtime.Serialization;
using System.Buffers;
using Akka.Actor;
using Akka.Hosting;
using Akka.Serialization;
using Demo.Core;
using Demo.Core.Chat;
using Demo.Core.Users;
using Microsoft.Extensions.DependencyInjection;
using MessagePack;
using Xunit;
using static Demo.Core.Chat.ChatCommands;
using static Demo.Core.Chat.ChatEvents;
using static Demo.Core.Users.UserCommands;
using static Demo.Core.Users.UserEvents;

namespace Demo.Tests;

public sealed class SerializationTests : Akka.Hosting.TestKit.TestKit
{
    protected override void ConfigureAkka(AkkaConfigurationBuilder builder, IServiceProvider provider) =>
        builder.WithChatSerializer();

    // Use the serializer selected by Hosting's bindings, including its id and wire manifest.
    private T RoundTrip<T>(T message) where T : notnull
    {
        var serializer = Sys.Serialization.FindSerializerFor(message);
        Assert.IsType<ChatSerializer>(serializer);
        Assert.Equal(120001, serializer.Identifier);
        var manifest = Serialization.ManifestFor(serializer, message);
        var bytes = serializer.ToBinary(message);
        var result = Sys.Serialization.Deserialize(bytes, serializer.Identifier, manifest);
        return Assert.IsType<T>(result);
    }

    private IEnumerable<IWithUserId> Messages(string text, Guid guid)
    {
        var id = new UserId(text);
        var name = new UserName(text);
        var messageId = new MessageId(guid);
        var content = new MessageContent(text);
        yield return new JoinChat(id, name, TestActor);
        yield return new ChangeUserName(id, name);
        yield return new LeaveChat(id);
        yield return new JoinedChat(id, name);
        yield return new ChangedUserName(id, name, new UserName("old"));
        yield return new LeftChat(id);
        yield return new SendMessage(messageId, id, content);
        yield return new EditMessage(messageId, id, content);
        yield return new MessageSent(messageId, id, content);
        yield return new MessageEdited(messageId, id, content);
    }

    [Theory]
    [InlineData("hello", "00000000-0000-0000-0000-000000000000")]
    [InlineData("こんにちは 👋 café\nsecond line\t\"quoted\"", "019a25d0-3515-7d7a-a23c-113cf6120192")]
    [InlineData("x", "ffffffff-ffff-ffff-ffff-ffffffffffff")]
    public void Every_protocol_message_round_trips(string text, string guid)
    {
        foreach (var message in Messages(text, Guid.Parse(guid)))
        {
            var serializer = Sys.Serialization.FindSerializerFor(message);
            Assert.IsType<ChatSerializer>(serializer);
            var result = Sys.Serialization.Deserialize(serializer.ToBinary(message), serializer.Identifier,
                Serialization.ManifestFor(serializer, message));
            Assert.Equal(message.GetType(), result.GetType());
            Assert.Equal(message, result);
        }
    }

    [Fact]
    public void Response_union_round_trips_every_protocol_member()
    {
        foreach (var message in Messages("union", Guid.NewGuid()))
        {
            var response = new ChatResponse(ChatResponseCode.Error, "rejected", message);
            Assert.Equal(response, RoundTrip(response));
        }
    }

    [Theory]
    [InlineData(ChatResponseCode.Success, null)]
    [InlineData(ChatResponseCode.NoOp, "")]
    [InlineData(ChatResponseCode.Error, "an error 👎")]
    public void Response_handles_enum_and_nullable_fields(ChatResponseCode code, string? error)
    {
        var response = new ChatResponse(code, error);
        Assert.Equal(response, RoundTrip(response));
    }

    [Fact]
    public void Actor_reference_survives_inside_a_response_and_can_receive_messages()
    {
        var command = new JoinChat(new UserId("a"), new UserName("Alice"), TestActor);
        var response = RoundTrip(new ChatResponse(ChatResponseCode.Success, OriginalCommand: command));
        var restored = Assert.IsType<JoinChat>(response.OriginalCommand);
        Assert.Equal(TestActor, restored.UserActor);
        restored.UserActor.Tell("still reachable");
        ExpectMsg("still reachable", cancellationToken: TestContext.Current.CancellationToken);
    }

    [Fact]
    public void Long_content_round_trips()
    {
        var command = new SendMessage(MessageId.Next(), new UserId("a"), new MessageContent(new string('x', 64_000)));
        Assert.Equal(command, RoundTrip(command));
    }

    [Theory]
    [InlineData("")]
    [InlineData(" ")]
    public void Empty_or_whitespace_wrapped_strings_round_trip_even_when_server_would_reject_them(string text)
    {
        var command = new ChangeUserName(new UserId(text), new UserName(text));
        Assert.Equal(command, RoundTrip(command));
    }

    [Fact]
    public void Empty_guid_and_unknown_enum_values_round_trip()
    {
        var command = new SendMessage(default, new UserId(""), new MessageContent("empty ids"));
        Assert.Equal(command, RoundTrip(command));
        var response = new ChatResponse((ChatResponseCode)42, OriginalCommand: command);
        Assert.Equal(response, RoundTrip(response));
    }

    [Fact]
    public void Null_in_a_required_wrapped_string_is_rejected()
    {
        var command = new LeaveChat(default);
        Assert.Throws<SerializationException>(() => RoundTrip(command));
    }

    [Fact]
    public void Unknown_fields_are_skipped_and_missing_optional_fields_use_defaults()
    {
        var serializer = Assert.IsType<ChatSerializer>(Sys.Serialization.FindSerializerFor(ChatResponse.Success));
        var original = serializer.ToBinary(ChatResponse.Success);
        var reader = new MessagePackReader(new ReadOnlySequence<byte>(original));
        var count = reader.ReadMapHeader();
        var buffer = new ArrayBufferWriter<byte>();
        var writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(count + 1);
        writer.WriteRaw(original.AsSpan((int)reader.Consumed));
        writer.Write(999);
        writer.WriteArrayHeader(2);
        writer.Write("future field");
        writer.Write(42);
        writer.Flush();
        Assert.Equal(ChatResponse.Success, serializer.FromBinary(buffer.WrittenSpan.ToArray(), "other-msg"));

        // Keep field 0 (the enum); omit nullable fields 1 and 2.
        buffer = new ArrayBufferWriter<byte>();
        writer = new MessagePackWriter(buffer);
        writer.WriteMapHeader(1);
        writer.Write(0);
        writer.Write((int)ChatResponseCode.NoOp);
        writer.Flush();
        Assert.Equal(new ChatResponse(ChatResponseCode.NoOp), serializer.FromBinary(buffer.WrittenSpan.ToArray(), "other-msg"));
    }

    [Fact]
    public void Hosting_registers_the_generated_serializer_and_disables_json_fallback()
    {
        Assert.Same(Sys, Host.Services.GetRequiredService<ActorSystem>());
        Assert.False(AppContext.TryGetSwitch("Akka.DynamicTypeLoading", out var enabled) && enabled);
        Assert.Throws<SerializationException>(() => Sys.Serialization.FindSerializerFor(new object()));
    }

    [Fact]
    public void Serializer_rejects_unknown_manifests_and_truncated_payloads()
    {
        var serializer = Assert.IsType<ChatSerializer>(Sys.Serialization.FindSerializerFor(ChatResponse.Success));
        Assert.Throws<SerializationException>(() => serializer.FromBinary([], "not-a-chat-message"));
        Assert.ThrowsAny<Exception>(() => serializer.FromBinary([0x81], "other-msg"));
    }
}
