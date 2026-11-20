/* =============================================================================
   ПРОЄКТ №3 — VideoRentalReviewsDb  (MongoDB)
   Контекст: «Відгуки та обговорення» (Reviews) — динамічна структура даних,
   де в одній колекції співіснують документи з різними наборами полів.
   -----------------------------------------------------------------------------
   Файл: 01-create-collections.js — створення колекцій, JSON Schema Validation
         та індексів.
   Запуск:  mongosh "mongodb://localhost:27017" 01-create-collections.js
   -----------------------------------------------------------------------------
   Моделювання зв'язків:
     embedding   — reviews.reviewer, reviews.comments[], watchlists.items[]
     referencing — reviews.discussionId -> discussions._id
     гібрид      — reviews: кілька останніх коментарів ВБУДОВАНО, а довга
                   дискусія винесена в окрему колекцію ЗА ПОСИЛАННЯМ
   Посилання на інші контексти — лише за ідентифікатором, без FK:
     reviews.movieId      -> Movies.Id   у VideoRentalCatalogDb (Проєкт №2)
     reviews.customer.id   -> Customers.Id у VideoRentalRentalsDb (Проєкт №1)
   ============================================================================= */

const db = db.getSiblingDB("VideoRentalReviewsDb");

print("=== VideoRentalReviewsDb: створення колекцій ===");

/* ---------------------------------------------------------------------------
   Ідемпотентність: прибираємо колекції, якщо скрипт запускають повторно.
   (seed-скрипт 02-seed.js ідемпотентний сам по собі — через upsert.)
   --------------------------------------------------------------------------- */
["reviews", "discussions", "watchlists", "customerProfiles"].forEach((name) => {
    if (db.getCollectionNames().includes(name)) {
        db.getCollection(name).drop();
        print(`  drop  ${name}`);
    }
});

/* =============================================================================
   1. reviews — відгуки на фільми.
      Головна колекція, що поєднує ВСІ прийоми моделювання.
      JSON Schema Validation контролює лише ОБОВ'ЯЗКОВЕ ядро документа
      (movieId, rating, reviewer) — решта полів вільна, що й дає гнучку схему.
   ============================================================================= */
db.createCollection("reviews", {
    validator: {
        $jsonSchema: {
            bsonType: "object",
            required: ["movieId", "rating", "reviewer", "createdAt"],
            properties: {
                movieId: {
                    bsonType: ["long", "int"],
                    description: "логічне посилання на Movies.Id у VideoRentalCatalogDb (без FK)"
                },
                movieTitle: {
                    bsonType: "string",
                    description: "денормалізовано з Каталогу для читання без міжсервісного виклику"
                },
                rating: {
                    bsonType: "int",
                    minimum: 1,
                    maximum: 5,
                    description: "оцінка від 1 до 5"
                },
                reviewer: {
                    bsonType: "object",
                    required: ["displayName"],
                    properties: {
                        customerId:    { bsonType: ["long", "int", "null"] },
                        displayName: { bsonType: "string" }
                    },
                    description: "ВБУДОВАНИЙ об'єкт — автор відгуку"
                },
                comments: {
                    bsonType: "array",
                    description: "ВБУДОВАНИЙ масив — кілька останніх коментарів",
                    items: {
                        bsonType: "object",
                        required: ["reviewer", "text"],
                        properties: {
                            // автор коментаря — такий самий вбудований об'єкт,
                            // як і автор відгуку (див. Лаб. 04, Value Object Reviewer)
                            reviewer: {
                                bsonType: "object",
                                required: ["displayName"],
                                properties: {
                                    customerId:    { bsonType: ["long", "int", "null"] },
                                    displayName: { bsonType: "string" }
                                }
                            },
                            text:      { bsonType: "string" },
                            createdAt: { bsonType: "date" },
                            likes:     { bsonType: "int" }
                        }
                    }
                },
                discussionId: {
                    bsonType: ["objectId", "null"],
                    description: "ПОСИЛАННЯ на колекцію discussions (необмежено довга ветка)"
                },
                createdAt: { bsonType: "date" },
                version:   { bsonType: "int", description: "оптимістична конкурентність (Лаб. 04)" }
            }
        }
    },
    validationLevel: "moderate",   // перевіряти нові та оновлювані документи
    validationAction: "error"
});
print("  create  reviews   (+ JSON Schema Validation)");

/* індекс під запит: «усі відгуки на фільм, найновіші спочатку» */
db.reviews.createIndex({ movieId: 1, createdAt: -1 }, { name: "ix_reviews_movie_created" });
/* індекс під запит: «середній рейтинг і розподіл оцінок фільму» (aggregation) */
db.reviews.createIndex({ movieId: 1, rating: -1 }, { name: "ix_reviews_movie_rating" });
/* індекс під запит: «усі відгуки конкретного клієнта» — розріджений,
   бо customerId є не в усіх документах (анонімні відгуки) */
db.reviews.createIndex({ "reviewer.customerId": 1 }, { name: "ix_reviews_customer", sparse: true });
/* текстовий індекс під запит: «повнотекстовий пошук по відгуках» */
db.reviews.createIndex({ text: "text", movieTitle: "text" }, { name: "ix_reviews_fulltext", default_language: "none" });
/* частковий індекс під запит: «відгуки з фото» (поле є лише в частини документів) */
db.reviews.createIndex({ createdAt: -1 },
    { name: "ix_reviews_with_photos", partialFilterExpression: { photos: { $exists: true } } });
print("  index   reviews x5");

/* =============================================================================
   2. discussions — довгі обговорення фільмів.
      Винесені в ОКРЕМУ колекцію саме тому, що кількість повідомлень
      не обмежена: вбудовувати тисячі повідомлень у відгук не можна
      (ліміт документа 16 МБ + постійне перезаписування).
   ============================================================================= */
db.createCollection("discussions", {
    validator: {
        $jsonSchema: {
            bsonType: "object",
            required: ["movieId", "topic", "createdAt"],
            properties: {
                movieId:    { bsonType: ["long", "int"] },
                movieTitle: { bsonType: "string" },
                topic:     { bsonType: "string" },
                isClosed:  { bsonType: "bool" },
                messages: {
                    bsonType: "array",
                    items: {
                        bsonType: "object",
                        required: ["reviewer", "text"],
                        properties: {
                            reviewer: {
                                bsonType: "object",
                                required: ["displayName"],
                                properties: {
                                    customerId:    { bsonType: ["long", "int", "null"] },
                                    displayName: { bsonType: "string" }
                                }
                            },
                            text:      { bsonType: "string" },
                            postedAt:  { bsonType: "date" },
                            replyTo:   { bsonType: ["int", "null"] }
                        }
                    }
                },
                createdAt: { bsonType: "date" }
            }
        }
    },
    validationLevel: "moderate",
    validationAction: "error"
});
print("  create  discussions   (+ JSON Schema Validation)");

/* індекс під запит: «відкриті обговорення фільму» */
db.discussions.createIndex({ movieId: 1, isClosed: 1 }, { name: "ix_discussions_movie_open" });
/* індекс під запит: «останні активні обговорення» */
db.discussions.createIndex({ lastActivityAt: -1 }, { name: "ix_discussions_activity", sparse: true });
print("  index   discussions x2");

/* =============================================================================
   3. watchlists — підбірки фільмів клієнтів.
      Класичний випадок ВБУДОВУВАННЯ: позиції підбірки завжди читаються
      разом із самою підбіркою і їх небагато.
   ============================================================================= */
db.createCollection("watchlists", {
    validator: {
        $jsonSchema: {
            bsonType: "object",
            required: ["title", "owner", "createdAt"],
            properties: {
                title:      { bsonType: "string" },
                isPublic:   { bsonType: "bool" },
                owner: {
                    bsonType: "object",
                    required: ["displayName"],
                    properties: {
                        customerId:    { bsonType: ["long", "int", "null"] },
                        displayName: { bsonType: "string" }
                    }
                },
                items: {
                    bsonType: "array",
                    description: "ВБУДОВАНИЙ масив позицій із денормалізованою назвою фільму",
                    items: {
                        bsonType: "object",
                        required: ["movieId"],
                        properties: {
                            movieId:    { bsonType: ["long", "int"] },
                            movieTitle: { bsonType: "string" },
                            note:      { bsonType: "string" },
                            addedAt:   { bsonType: "date" },
                            status:    { bsonType: "string", enum: ["planned", "reading", "finished", "dropped"] }
                        }
                    }
                },
                createdAt: { bsonType: "date" }
            }
        }
    },
    validationLevel: "moderate",
    validationAction: "error"
});
print("  create  watchlists   (+ JSON Schema Validation)");

/* індекс під запит: «публічні підбірки, у яких є конкретний фільм» —
   багатоключовий індекс по вбудованому масиву */
db.watchlists.createIndex({ "items.movieId": 1, isPublic: 1 }, { name: "ix_lists_item_movie" });
/* індекс під запит: «підбірки клієнта» */
db.watchlists.createIndex({ "owner.customerId": 1 }, { name: "ix_lists_owner", sparse: true });
print("  index   watchlists x2");

/* =============================================================================
   4. customerProfiles — профілі клієнтів у контексті Відгуків.
      ДЕМОНСТРАЦІЯ ГНУЧКОЇ СХЕМИ: валідатор вимагає лише customerId
      і displayName, тому документи можуть мати будь-які додаткові поля
      (badges, preferences, socialLinks, stats) — див. 02-seed.js.
   ============================================================================= */
db.createCollection("customerProfiles", {
    validator: {
        $jsonSchema: {
            bsonType: "object",
            required: ["customerId", "displayName"],
            properties: {
                customerId:    { bsonType: ["long", "int"] },
                displayName: { bsonType: "string" }
            }
        }
    },
    validationLevel: "moderate",
    validationAction: "error"
});
print("  create  customerProfiles   (+ JSON Schema Validation, гнучка схема)");

db.customerProfiles.createIndex({ customerId: 1 }, { name: "ix_profiles_customer", unique: true });
print("  index   customerProfiles x1");

print("");
print("=== Готово. Колекції: " + db.getCollectionNames().join(", ") + " ===");
print("Далі: mongosh \"mongodb://localhost:27017\" 02-seed.js");
