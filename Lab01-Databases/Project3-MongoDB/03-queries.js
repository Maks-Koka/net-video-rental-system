/* =============================================================================
   ПРОЄКТ №3 — VideoRentalReviewsDb : запити, агрегації та перевірка критеріїв
   -----------------------------------------------------------------------------
   Запуск:  mongosh "mongodb://localhost:27017" 03-queries.js
   Кожен блок відповідає пункту розділу «Перед здачею перевірте себе» (Лаб. 01).
   ============================================================================= */

const db = db.getSiblingDB("VideoRentalReviewsDb");
const line = (t) => print("\n=============================================================\n " + t + "\n=============================================================");

/* =============================================================================
   1. ГНУЧКА СХЕМА: документи з РІЗНИМИ наборами полів в одній колекції
      (критерій приймання: db.<колекція>.find() повертає різні структури)
   ============================================================================= */
line("1. Гнучка схема: різні набори полів в колекції reviews");
/* БЕЗ проєкції — інакше ми побачили б лише відфільтровані поля, а не
   справжній набір полів кожного документа. */
db.reviews.find().forEach((doc) => {
    const keys = Object.keys(doc).sort().join(", ");
    print(`  "${doc.movieTitle}"\n      поля: ${keys}`);
});
print("\n  Повний перелік полів, що зустрічаються в колекції:");
const allKeys = db.reviews.aggregate([
    { $project: { kv: { $objectToArray: "$$ROOT" } } },
    { $unwind: "$kv" },
    { $group: { _id: "$kv.k", docs: { $sum: 1 } } },
    { $sort: { docs: -1, _id: 1 } }
]).toArray();
allKeys.forEach((k) => print(`    ${k._id.padEnd(18)} присутнє в ${k.docs} з ${db.reviews.countDocuments()} документів`));

line("1b. Гнучка схема в customerProfiles");
db.customerProfiles.find().forEach((doc) => {
    print(`  customerId=${doc.customerId}  поля: ${Object.keys(doc).sort().join(", ")}`);
});

/* =============================================================================
   2. EMBEDDING — вбудовані об'єкти та масиви читаються ОДНИМ запитом
   ============================================================================= */
line("2. Embedding: режисер і коментарі приходять разом із відгуком (1 запит)");
printjson(db.reviews.findOne(
    { _id: ObjectId("66f0c20000000000000000b1") },
    { movieTitle: 1, "author.displayName": 1, "comments.author": 1, "comments.text": 1 }
));

/* =============================================================================
   3. REFERENCING — довга дискусія лежить в окремій колекції; дотягуємо $lookup
   ============================================================================= */
line("3. Referencing: $lookup відгук -> discussions за discussionId");
db.reviews.aggregate([
    { $match: { discussionId: { $ne: null, $exists: true } } },
    { $lookup: {
        from: "discussions",
        localField: "discussionId",
        foreignField: "_id",
        as: "discussion"
    } },
    { $unwind: "$discussion" },
    { $project: {
        _id: 0,
        movieTitle: 1,
        reviewRating: "$rating",
        embeddedComments: { $size: { $ifNull: ["$comments", []] } },
        discussionTopic: "$discussion.topic",
        discussionMessages: { $size: { $ifNull: ["$discussion.messages", []] } },
        discussionClosed: "$discussion.isClosed"
    } }
]).forEach((d) => printjson(d));
print("  >> ГІБРИД: кілька коментарів ВБУДОВАНО у відгук (швидке читання),");
print("  >> а необмежено довга ветка — ЗА ПОСИЛАННЯМ в окремій колекції.");

/* =============================================================================
   4. АГРЕГАЦІЯ: середній рейтинг і розподіл оцінок по фільмах
   ============================================================================= */
line("4. Aggregation pipeline: рейтинг фільмів за відгуками");
db.reviews.aggregate([
    { $group: {
        _id: { movieId: "$movieId", movieTitle: "$movieTitle" },
        reviews: { $sum: 1 },
        avgRating: { $avg: "$rating" },
        minRating: { $min: "$rating" },
        maxRating: { $max: "$rating" },
        commentsTotal: { $sum: { $size: { $ifNull: ["$comments", []] } } }
    } },
    { $project: {
        _id: 0,
        movieId: "$_id.movieId",
        movieTitle: "$_id.movieTitle",
        reviews: 1,
        avgRating: { $round: ["$avgRating", 2] },
        minRating: 1, maxRating: 1, commentsTotal: 1
    } },
    { $sort: { avgRating: -1, movieTitle: 1 } }
]).forEach((d) => printjson(d));

/* =============================================================================
   5. АГРЕГАЦІЯ по вбудованому масиву: найактивніші комментатори
   ============================================================================= */
line("5. Aggregation + $unwind: топ коментаторів за лайками");
db.reviews.aggregate([
    { $unwind: "$comments" },
    { $group: {
        _id: "$comments.author",
        comments: { $sum: 1 },
        likes: { $sum: { $ifNull: ["$comments.likes", 0] } }
    } },
    { $sort: { likes: -1, comments: -1 } }
]).forEach((d) => printjson(d));

/* =============================================================================
   6. ДЕНОРМАЛІЗАЦІЯ: movieTitle читається без звернення до Каталогу
   ============================================================================= */
line("6. Денормалізація movieTitle (політика дублювання)");
db.reviews.find({}, { _id: 0, movieId: 1, movieTitle: 1 }).sort({ movieId: 1 }).forEach((d) =>
    print(`  movieId=${d.movieId}  movieTitle="${d.movieTitle}"  <- копія з VideoRentalCatalogDb, оновлюється подіями`));
print("  >> Жодного FOREIGN KEY на VideoRentalCatalogDb не існує і не може існувати:");
print("  >> це інша база (і взагалі інша СУБД). Цілісність — на рівні подій.");

/* =============================================================================
   7. ЗАПИТ ПО ВБУДОВАНОМУ МАСИВУ: підбірки, в яких є конкретний фільм
   ============================================================================= */
line("7. Запит по вбудованому масиву (multikey index ix_lists_item_movie)");
db.watchlists.find(
    { "items.movieId": NumberLong("1") },
    { _id: 0, title: 1, isPublic: 1, "owner.displayName": 1 }
).forEach((d) => printjson(d));

line("7b. $elemMatch: позиції підбірки зі статусом 'watching'");
db.watchlists.aggregate([
    { $unwind: "$items" },
    { $match: { "items.status": "watching" } },
    { $project: { _id: 0, list: "$title", movie: "$items.movieTitle", status: "$items.status" } }
]).forEach((d) => printjson(d));

/* =============================================================================
   8. ЧАСТКОВИЙ/РОЗРІДЖЕНИЙ ІНДЕКС: відгуки з фото
   ============================================================================= */
line("8. Відгуки, де поле photos взагалі існує (partial index)");
db.reviews.find({ photos: { $exists: true } }, { _id: 0, movieTitle: 1, photos: 1 }).forEach((d) => printjson(d));

/* =============================================================================
   9. ПЕРЕВІРКА JSON SCHEMA VALIDATION — некоректний документ має бути відхилений
   ============================================================================= */
line("9. JSON Schema Validation: rating=9 має бути відхилено");
try {
    db.reviews.insertOne({
        movieId: NumberLong("1"),
        author: { displayName: "Порушник" },
        rating: NumberInt(9),                     // поза межами 1..5
        createdAt: new Date()
    });
    print("  !!! ПОМИЛКА ТЕСТУ: документ вставився, хоча rating=9");
} catch (e) {
    print(`  OK  Валідатор відхилив документ. code=${e.code} — ${String(e.message).substring(0, 90)}`);
}

line("9b. JSON Schema Validation: відсутнє обов'язкове поле author");
try {
    db.reviews.insertOne({ movieId: NumberLong("1"), rating: NumberInt(4), createdAt: new Date() });
    print("  !!! ПОМИЛКА ТЕСТУ: документ без author вставився");
} catch (e) {
    print(`  OK  Валідатор відхилив документ. code=${e.code} — ${String(e.message).substring(0, 90)}`);
}

line("9c. А ось документ із НОВИМ, не описаним у схемі полем — приймається");
const probeId = ObjectId("66f0c20000000000000000bf");
db.reviews.replaceOne(
    { _id: probeId },
    {
        _id: probeId,
        movieId: NumberLong("6"),
        movieTitle: "Мої думки тихі",
        author: { displayName: "Тест гнучкості" },
        rating: NumberInt(4),
        createdAt: new Date(),
        videoReviewUrl: "review-6.mp4",           // поля немає в жодному валідаторі
        watchedWhile: { activity: "evening", city: "Київ" }
    },
    { upsert: true }
);
print("  OK  Документ із полями videoReviewUrl / watchedWhile збережено.");
print("  >> Саме це й означає «гнучка схема»: ядро валідується, розширення вільні.");
db.reviews.deleteOne({ _id: probeId });           // прибираємо, щоб скрипт був повторюваним

/* =============================================================================
   10. ІНДЕКСИ: перелік із призначенням
   ============================================================================= */
line("10. Індекси всіх колекцій");
["reviews", "discussions", "watchlists", "customerProfiles"].forEach((name) => {
    print(`\n  ${name}:`);
    db.getCollection(name).getIndexes().forEach((ix) => {
        const extra = [];
        if (ix.unique) extra.push("unique");
        if (ix.sparse) extra.push("sparse");
        if (ix.partialFilterExpression) extra.push("partial");
        print(`    ${ix.name.padEnd(26)} ${JSON.stringify(ix.key)} ${extra.length ? "[" + extra.join(",") + "]" : ""}`);
    });
});

/* =============================================================================
   11. explain(): індекс реально використовується (IXSCAN, а не COLLSCAN)
   ============================================================================= */
line("11. explain(): запит по movieId використовує індекс");
const plan = db.reviews.find({ movieId: NumberLong("1") }).sort({ createdAt: -1 })
    .explain("queryPlanner").queryPlanner.winningPlan;
print("  winningPlan: " + JSON.stringify(plan).substring(0, 400));

line("ПЕРЕВІРКА ЗАВЕРШЕНА");
print("Підсумок колекцій:");
["reviews", "discussions", "watchlists", "customerProfiles"].forEach((n) =>
    print(`  ${n.padEnd(16)} ${db.getCollection(n).countDocuments()} документів`));
