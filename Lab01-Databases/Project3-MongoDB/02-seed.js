/* =============================================================================
   ПРОЄКТ №3 — VideoRentalReviewsDb : тестові документи
   -----------------------------------------------------------------------------
   Запуск:  mongosh "mongodb://localhost:27017" 02-seed.js
   ІДЕМПОТЕНТНІСТЬ: усі вставки через replaceOne({..}, {..}, {upsert:true})
   з детермінованим _id, тому повторний запуск НЕ створює дублікатів.
   -----------------------------------------------------------------------------
   Документи свідомо мають РІЗНІ набори полів в одній колекції:
     reviews #1 — повний «гібрид»: вбудований автор + коментарі + посилання
     reviews #2 — з масивом photos і tags (у #1 їх немає)
     reviews #3 — анонімний: немає reviewer.customerId, немає comments
     reviews #4 — зі вкладеним об'єктом ratings (детальні оцінки) замість тексту
   ============================================================================= */

const db = db.getSiblingDB("VideoRentalReviewsDb");

/* детерміновані ObjectId — щоб посилання між колекціями були стабільними */
const DISC_KOBZAR   = ObjectId("66f0c30000000000000000a1");
const DISC_INTERNAT = ObjectId("66f0c30000000000000000a2");

print("=== VideoRentalReviewsDb: наповнення тестовими даними ===");

/* =============================================================================
   discussions — спочатку, бо на них посилаються reviews
   ============================================================================= */
db.discussions.replaceOne(
    { _id: DISC_KOBZAR },
    {
        _id: DISC_KOBZAR,
        movieId: NumberLong("1"),
        movieTitle: "Тіні забутих предків",                      // денормалізовано з Каталогу
        topic: "Чи варто показувати «Тіні забутих предків» у школі?",
        isClosed: false,
        createdAt: new Date("2026-09-10T09:00:00Z"),
        lastActivityAt: new Date("2026-11-18T14:30:00Z"),
        messages: [                               // вбудований масив повідомлень
            { reviewer: "Олена",  text: "Класика, яку варто переглядати дорослим.", postedAt: new Date("2026-09-10T09:05:00Z"), replyTo: null },
            { reviewer: "Андрій", text: "У школі складно, але з хорошим учителем — так.", postedAt: new Date("2026-09-11T10:20:00Z"), replyTo: 0 },
            { reviewer: "Марія",  text: "Погоджуюсь, контекст важливіший за заучування.", postedAt: new Date("2026-11-18T14:30:00Z"), replyTo: 1 }
        ]
    },
    { upsert: true }
);

db.discussions.replaceOne(
    { _id: DISC_INTERNAT },
    {
        _id: DISC_INTERNAT,
        movieId: NumberLong("4"),
        movieTitle: "Поводир",
        topic: "Мова війни у фільмі «Кіборги»",
        isClosed: true,                           // закрите обговорення
        closedReason: "Тему вичерпано",           // поле є ЛИШЕ в цьому документі
        createdAt: new Date("2026-10-02T12:00:00Z"),
        lastActivityAt: new Date("2026-10-20T18:00:00Z"),
        messages: [
            { reviewer: "Ігор",   text: "Найсильніше — відсутність пафосу.", postedAt: new Date("2026-10-02T12:10:00Z"), replyTo: null },
            { reviewer: "Олена",  text: "Саме тому він і болить.",            postedAt: new Date("2026-10-20T18:00:00Z"), replyTo: 0 }
        ]
    },
    { upsert: true }
);
print(`  discussions: ${db.discussions.countDocuments()} документів`);

/* =============================================================================
   reviews — ГОЛОВНА демонстрація: embedding + referencing + гібрид + гнучка схема
   ============================================================================= */

/* --- #1 ГІБРИД: вбудований автор, вбудовані коментарі ТА посилання на дискусію --- */
db.reviews.replaceOne(
    { _id: ObjectId("66f0c20000000000000000b1") },
    {
        _id: ObjectId("66f0c20000000000000000b1"),
        movieId: NumberLong("1"),                    // посилання на Каталог (без FK)
        movieTitle: "Тіні забутих предків",                      // ДЕНОРМАЛІЗОВАНО для читання
        reviewer: {                                 // ВБУДОВАНИЙ об'єкт
            customerId: NumberLong("1"),              // логічне посилання на Customers.Id
            displayName: "Олена К."
        },
        rating: NumberInt(5),
        text: "Реставрована версія з якісним звуком — дивитися набагато приємніше.",
        comments: [                               // ВБУДОВАНИЙ масив
            { reviewer: "Андрій", text: "Згоден, реставрація врятувала.", createdAt: new Date("2026-09-15T08:00:00Z"), likes: NumberInt(3) },
            { reviewer: "Марія",  text: "А яка саме версія?",          createdAt: new Date("2026-09-16T09:30:00Z"), likes: NumberInt(0) }
        ],
        discussionId: DISC_KOBZAR,                // ПОСИЛАННЯ на окрему колекцію
        createdAt: new Date("2026-09-14T07:45:00Z")
    },
    { upsert: true }
);

/* --- #2 ГНУЧКА СХЕМА: є photos і tags, яких немає в інших документах --- */
db.reviews.replaceOne(
    { _id: ObjectId("66f0c20000000000000000b2") },
    {
        _id: ObjectId("66f0c20000000000000000b2"),
        movieId: NumberLong("4"),
        movieTitle: "Поводир",
        reviewer: { customerId: NumberLong("2"), displayName: "Андрій Ш." },
        rating: NumberInt(5),
        text: "Подивився за один вечір. Мова суха і від цього ще сильніша.",
        photos: ["internat-cover.jpg", "internat-page-77.jpg"],   // поле лише тут
        tags: ["війна", "сучасна проза", "мушрід"],                // поле лише тут
        comments: [
            { reviewer: "Ігор", text: "Варто дивитися разом із «Довбушем».", createdAt: new Date("2026-11-02T11:00:00Z"), likes: NumberInt(5) }
        ],
        discussionId: DISC_INTERNAT,
        createdAt: new Date("2026-11-01T19:20:00Z")
    },
    { upsert: true }
);

/* --- #3 АНОНІМНИЙ: немає reviewer.customerId, немає comments, немає discussionId --- */
db.reviews.replaceOne(
    { _id: ObjectId("66f0c20000000000000000b3") },
    {
        _id: ObjectId("66f0c20000000000000000b3"),
        movieId: NumberLong("2"),
        movieTitle: "Земля",
        reviewer: { displayName: "Анонім" },        // customerId ВІДСУТНІЙ
        rating: NumberInt(4),
        text: "Динамічно, але фінал надто швидкий.",
        createdAt: new Date("2026-10-05T16:00:00Z")
    },
    { upsert: true }
);

/* --- #4 ІНША СТРУКТУРА: замість text — вкладений об'єкт детальних оцінок --- */
db.reviews.replaceOne(
    { _id: ObjectId("66f0c20000000000000000b4") },
    {
        _id: ObjectId("66f0c20000000000000000b4"),
        movieId: NumberLong("5"),
        movieTitle: "Кіборги",
        reviewer: { customerId: NumberLong("3"), displayName: "Марія Б." },
        rating: NumberInt(4),
        ratings: {                                // ВКЛАДЕНИЙ ОБ'ЄКТ — складний агрегат
            plot: NumberInt(5),
            language: NumberInt(4),
            historicalAccuracy: NumberInt(3),
            pacing: NumberInt(4)
        },
        watchingContext: {                         // другий рівень вкладеності
            finishedAt: new Date("2026-11-12T00:00:00Z"),
            format: "bluray",
            rewatching: false
        },
        comments: [],                             // порожній масив — теж варіант
        createdAt: new Date("2026-11-12T20:00:00Z")
    },
    { upsert: true }
);

/* --- #5 мінімальний: лише обов'язкове ядро за JSON Schema --- */
db.reviews.replaceOne(
    { _id: ObjectId("66f0c20000000000000000b5") },
    {
        _id: ObjectId("66f0c20000000000000000b5"),
        movieId: NumberLong("3"),
        movieTitle: "Пропала грамота",
        reviewer: { displayName: "Клієнт без профілю" },
        rating: NumberInt(3),
        createdAt: new Date("2026-11-17T10:00:00Z")
    },
    { upsert: true }
);
print(`  reviews: ${db.reviews.countDocuments()} документів (з різними наборами полів)`);

/* =============================================================================
   watchlists — ВБУДОВУВАННЯ позицій
   ============================================================================= */
db.watchlists.replaceOne(
    { _id: ObjectId("66f0c40000000000000000c1") },
    {
        _id: ObjectId("66f0c40000000000000000c1"),
        title: "Український кінематограф на зиму",
        isPublic: true,
        owner: { customerId: NumberLong("1"), displayName: "Олена К." },
        createdAt: new Date("2026-09-20T08:00:00Z"),
        items: [                                  // ВБУДОВАНИЙ масив із денормалізацією
            { movieId: NumberLong("1"), movieTitle: "Тіні забутих предків",     status: "finished", addedAt: new Date("2026-09-20T08:00:00Z"), note: "Переглянути сцену в Карпатах" },
            { movieId: NumberLong("3"), movieTitle: "Пропала грамота",      status: "watching",  addedAt: new Date("2026-10-01T08:00:00Z") },
            { movieId: NumberLong("2"), movieTitle: "Земля",  status: "planned",  addedAt: new Date("2026-10-15T08:00:00Z") }
        ]
    },
    { upsert: true }
);

db.watchlists.replaceOne(
    { _id: ObjectId("66f0c40000000000000000c2") },
    {
        _id: ObjectId("66f0c40000000000000000c2"),
        title: "Приватний список",
        isPublic: false,
        owner: { displayName: "Анонім" },         // немає customerId
        createdAt: new Date("2026-11-05T08:00:00Z"),
        items: [
            { movieId: NumberLong("4"), movieTitle: "Поводир", status: "planned", addedAt: new Date("2026-11-05T08:00:00Z") }
        ]
    },
    { upsert: true }
);
print(`  watchlists: ${db.watchlists.countDocuments()} документів`);

/* =============================================================================
   customerProfiles — НАЙЯСКРАВІША демонстрація гнучкої схеми:
   три документи в одній колекції з принципово різними наборами полів.
   ============================================================================= */
db.customerProfiles.replaceOne(
    { customerId: NumberLong("1") },
    {
        customerId: NumberLong("1"),
        displayName: "Олена К.",
        bio: "Дивлюся українську класику й сучасне кіно.",
        badges: ["top-reviewer", "100-movies"],
        stats: { reviewsCount: NumberInt(1), averageRating: 5.0, listsCount: NumberInt(1) },
        preferences: { favouriteGenres: ["Комедія", "Класика"], emailDigest: true, theme: "light" }
    },
    { upsert: true }
);

db.customerProfiles.replaceOne(
    { customerId: NumberLong("2") },
    {
        customerId: NumberLong("2"),
        displayName: "Андрій Ш.",
        socialLinks: { telegram: "@andrii_watches", letterboxd: "andrii-sh" },   // поля немає в інших
        stats: { reviewsCount: NumberInt(1), averageRating: 5.0 }
    },
    { upsert: true }
);

db.customerProfiles.replaceOne(
    { customerId: NumberLong("3") },
    {
        customerId: NumberLong("3"),
        displayName: "Марія Б."                   // ЛИШЕ обов'язкові поля
    },
    { upsert: true }
);
print(`  customerProfiles: ${db.customerProfiles.countDocuments()} документів`);

print("");
print("=== Готово. Повторний запуск цього скрипта дублікатів не створює. ===");
print("Далі: mongosh \"mongodb://localhost:27017\" 03-queries.js");
