using VideoRental.Reviews.Domain.Common;
using VideoRental.Reviews.Domain.Entities;
using VideoRental.Reviews.Domain.ValueObjects;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;
using MongoDB.Bson.Serialization.Serializers;

namespace VideoRental.Reviews.Infrastructure.Persistence;

/// <summary>
/// Серіалізатор <see cref="Rating"/>: у документі зберігається <b>звичайне
/// число</b>, а не вкладений об'єкт.
/// </summary>
/// <remarks>
/// Так документ лишається сумісним зі схемою з Лаб. 01, де
/// <c>rating</c> описано як <c>bsonType: "int"</c> з межами 1..5, а в домені
/// оцінка все одно є повноцінним Value Object із власною валідацією.
/// </remarks>
public sealed class RatingSerializer : SerializerBase<Rating>
{
    public override void Serialize(
        BsonSerializationContext context, BsonSerializationArgs args, Rating value)
    {
        if (value is null) context.Writer.WriteNull();
        else context.Writer.WriteInt32(value.Value);
    }

    public override Rating Deserialize(
        BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var customer = context.Reader;
        return customer.GetCurrentBsonType() switch
        {
            BsonType.Int32  => new Rating(customer.ReadInt32()),
            BsonType.Int64  => new Rating((int)customer.ReadInt64()),
            BsonType.Double => new Rating((int)customer.ReadDouble()),
            var other => throw new FormatException($"Cannot deserialize Rating from BsonType {other}")
        };
    }
}

/// <summary>
/// Серіалізатор Value Object <see cref="Reviewer"/>.
/// </summary>
/// <remarks>
/// Власний серіалізатор, а не <c>BsonClassMap.MapCreator</c>, тому що
/// <see cref="Reviewer"/> справді незмінний (обидві властивості — лише
/// <c>get</c>), а creator map вимагає, щоб УСІ його аргументи були присутні
/// в документі. Анонімні відгуки з Лаб. 01 поля <c>customerId</c> не мають
/// узагалі — для них creator не знайшовся б. Серіалізатор читає документ
/// вручну й толерантний до відсутніх та зайвих полів.
/// </remarks>
public sealed class ReviewerSerializer : SerializerBase<Reviewer>
{
    public override void Serialize(
        BsonSerializationContext context, BsonSerializationArgs args, Reviewer value)
    {
        if (value is null) { context.Writer.WriteNull(); return; }

        var writer = context.Writer;
        writer.WriteStartDocument();

        if (value.CustomerId is { } customerId)
        {
            writer.WriteName("customerId");
            writer.WriteInt64(customerId);
        }

        writer.WriteName("displayName");
        writer.WriteString(value.DisplayName);
        writer.WriteEndDocument();
    }

    public override Reviewer Deserialize(
        BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var customer = context.Reader;

        if (customer.GetCurrentBsonType() == BsonType.Null)
        {
            customer.ReadNull();
            return null!;
        }

        long? customerId = null;
        string displayName = "";

        customer.ReadStartDocument();
        while (customer.ReadBsonType() != BsonType.EndOfDocument)
        {
            switch (customer.ReadName())
            {
                case "customerId":
                    customerId = customer.GetCurrentBsonType() switch
                    {
                        BsonType.Null  => SkipNull(customer),
                        BsonType.Int32 => customer.ReadInt32(),
                        _              => customer.ReadInt64()
                    };
                    break;
                case "displayName":
                    displayName = customer.ReadString();
                    break;
                default:
                    customer.SkipValue();        // гнучка схема: зайві поля ігноруємо
                    break;
            }
        }
        customer.ReadEndDocument();

        return new Reviewer(customerId, displayName);

        static long? SkipNull(IBsonReader r) { r.ReadNull(); return null; }
    }
}

/// <summary>
/// Налаштування BSON-серіалізації доменних типів.
/// </summary>
/// <remarks>
/// <para>
/// <b>Чому мапінг тут, а не атрибутами на класах.</b> Атрибути
/// (<c>[BsonElement]</c>, <c>[BsonId]</c>) прив'язали б доменні класи до
/// деталей зберігання й порушили головне правило Clean Architecture: Domain
/// не повинен знати, як і де його зберігають. Тому мапінг налаштовується
/// ззовні — у шарі Infrastructure, через <see cref="BsonClassMap"/>.
/// </para>
/// <para>
/// <b>Чому членів мапимо явно, а не через <c>AutoMap()</c>.</b> AutoMap
/// знаходить публічний конструктор <c>Review(MovieRef, Reviewer, int, string?)</c>
/// і намагається зробити з нього creator map, але зіставити параметр
/// <c>int rating</c> із властивістю типу <see cref="Rating"/> не може.
/// Явне мапування прибирає цю неоднозначність: драйвер створює об'єкт
/// приватним конструктором без параметрів і заповнює поля напряму.
/// </para>
/// <para>Реєструється <b>один раз</b> під час старту, до першого звернення до бази.</para>
/// </remarks>
public static class MongoMappings
{
    private static readonly object Gate = new();
    private static bool _registered;

    public static void Register()
    {
        if (_registered) return;

        lock (Gate)
        {
            if (_registered) return;

            // 1. Конвенції: camelCase (як у схемі Лаб. 01); зайві поля
            //    ігноруються — гнучка схема NoSQL означає, що в документі
            //    можуть бути поля, яких немає в класі.
            ConventionRegistry.Register(
                "library-reviews",
                new ConventionPack
                {
                    new CamelCaseElementNameConvention(),
                    new IgnoreExtraElementsConvention(true),
                    new IgnoreIfNullConvention(true)
                },
                _ => true);

            // 2. DateTime завжди в UTC
            BsonSerializer.TryRegisterSerializer(new DateTimeSerializer(DateTimeKind.Utc));

            // 3. Rating -> звичайне число (сумісність зі схемою Лаб. 01)
            BsonSerializer.TryRegisterSerializer(new RatingSerializer());

            // 4. Reviewer -> вбудований об'єкт через власний серіалізатор
            //    (толерантний до анонімних авторів без customerId).
            BsonSerializer.TryRegisterSerializer(new ReviewerSerializer());

            BsonClassMap.RegisterClassMap<Comment>(cm =>
            {
                cm.SetIgnoreExtraElements(true);
                cm.MapProperty(c => c.Reviewer).SetElementName("reviewer");
                cm.MapProperty(c => c.Text).SetElementName("text");
                cm.MapProperty(c => c.CreatedAt).SetElementName("createdAt");
                cm.MapProperty(c => c.Likes).SetElementName("likes");
            });

            BsonClassMap.RegisterClassMap<DiscussionMessage>(cm =>
            {
                cm.SetIgnoreExtraElements(true);
                cm.MapProperty(m => m.Reviewer).SetElementName("reviewer");
                cm.MapProperty(m => m.Text).SetElementName("text");
                cm.MapProperty(m => m.PostedAt).SetElementName("postedAt");
                cm.MapProperty(m => m.ReplyTo).SetElementName("replyTo");
            });

            // 5. Спільні поля базового класу. Реєструємо окремою картою, бо
            //    MapIdMember вимагає член, оголошений САМЕ в цьому класі.
            BsonClassMap.RegisterClassMap<BaseEntity>(cm =>
            {
                cm.SetIsRootClass(true);          // похідні класи успадковують цю карту
                cm.MapIdProperty(e => e.Id);
                cm.MapProperty(e => e.CreatedAt).SetElementName("createdAt");
                cm.MapProperty(e => e.UpdatedAt).SetElementName("updatedAt");
                cm.MapProperty(e => e.Version).SetElementName("version");
            });

            // 6. Сутності. Мапимо явно:
            //    - movieId / movieTitle лежать на верхньому рівні (схема Лаб. 01);
            //    - приватні поля-колекції — бо публічні властивості віддають
            //      IReadOnlyCollection, писати в них драйвер не може.
            BsonClassMap.RegisterClassMap<Review>(cm =>
            {
                cm.SetIgnoreExtraElements(true);
                cm.MapField("_movieId").SetElementName("movieId");
                cm.MapField("_movieTitle").SetElementName("movieTitle");
                cm.MapProperty(r => r.Reviewer).SetElementName("reviewer");
                cm.MapProperty(r => r.Rating).SetElementName("rating");
                cm.MapProperty(r => r.Text).SetElementName("text");
                cm.MapProperty(r => r.DiscussionId).SetElementName("discussionId");
                cm.MapField("_comments").SetElementName("comments");
                cm.MapField("_photos").SetElementName("photos");
                cm.MapField("_tags").SetElementName("tags");
            });

            BsonClassMap.RegisterClassMap<Discussion>(cm =>
            {
                cm.SetIgnoreExtraElements(true);
                cm.MapField("_movieId").SetElementName("movieId");
                cm.MapField("_movieTitle").SetElementName("movieTitle");
                cm.MapProperty(d => d.Topic).SetElementName("topic");
                cm.MapProperty(d => d.IsClosed).SetElementName("isClosed");
                cm.MapProperty(d => d.ClosedReason).SetElementName("closedReason");
                cm.MapProperty(d => d.LastActivityAt).SetElementName("lastActivityAt");
                cm.MapField("_messages").SetElementName("messages");
            });

            _registered = true;
        }
    }
}
