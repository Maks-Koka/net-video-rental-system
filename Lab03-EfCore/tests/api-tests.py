#!/usr/bin/env python3
"""
Автоматична перевірка критеріїв приймання Лабораторної роботи № 03
для мікросервісу «Каталог фільмів» (VideoRental.Catalog.Api, EF Core).

Запуск (API має бути піднятий):
    py -I tests/api-tests.py                      # http://localhost:5290
    py -I tests/api-tests.py http://localhost:5100

Кожен блок відповідає пункту розділу «Перед здачею перевірте себе».
Скрипт самодостатній: лише стандартна відеопрокат.
"""

import json
import random
import sys
import urllib.error
import urllib.parse
import urllib.request

try:
    sys.stdout.reconfigure(encoding="utf-8")
    sys.stderr.reconfigure(encoding="utf-8")
except Exception:
    pass

BASE = sys.argv[1].rstrip("/") if len(sys.argv) > 1 else "http://localhost:5290"
SUFFIX = random.randint(1000, 9999)

passed: list[str] = []
failed: list[str] = []


def call(method: str, path: str, body=None):
    data = json.dumps(body, ensure_ascii=False).encode("utf-8") if body is not None else None

    # сегменти шляху (можуть містити кирилицю) і значення query-параметрів
    # (можуть містити пробіли та свідомо «небезпечні» символи) кодуємо окремо
    route, sep, query = path.partition("?")
    route = urllib.parse.quote(route, safe="/")

    if sep:
        pairs = []
        for part in query.split("&"):
            k, _, v = part.partition("=")
            pairs.append(f"{k}={urllib.parse.quote(v, safe='')}")
        path = route + "?" + "&".join(pairs)
    else:
        path = route

    req = urllib.request.Request(
        BASE + path, data=data, method=method,
        headers={"Content-Type": "application/json; charset=utf-8", "Accept": "application/json"},
    )
    try:
        with urllib.request.urlopen(req) as r:
            raw = r.read().decode("utf-8")
            return r.status, (json.loads(raw) if raw else None), dict(r.headers)
    except urllib.error.HTTPError as e:
        raw = e.read().decode("utf-8")
        try:
            return e.code, json.loads(raw), dict(e.headers)
        except json.JSONDecodeError:
            return e.code, raw, dict(e.headers)


def check(name: str, condition: bool, detail: str = ""):
    if condition:
        passed.append(name)
        print(f"  [OK]   {name}")
    else:
        failed.append(f"{name} :: {detail}")
        print(f"  [FAIL] {name}")
        if detail:
            print(f"         {detail}")


def section(title: str):
    print()
    print("=" * 72)
    print(f" {title}")
    print("=" * 72)


# =============================================================================
section("0. Сервіс доступний, міграції застосовано, сідінг виконано")
# =============================================================================
st, body, _ = call("GET", "/health")
check("GET /health -> 200 Healthy", st == 200 and body and body.get("status") == "Healthy",
      f"status={st} body={body}")

# --- Прибирання за попередніми прогонами ---------------------------------
# Скрипт створює фільми та режисерів із префіксом «Автотест». Щоб його можна було
# запускати багато разів поспіль, спершу прибираємо залишки минулих прогонів.
_, leftovers, _ = call("GET", "/api/movies?search=Автотест&pageSize=100")
removed_movies = 0
if isinstance(leftovers, dict):
    for b in leftovers.get("items", []):
        if b["title"].startswith("Автотест"):
            call("DELETE", f"/api/movies/{b['id']}")
            removed_movies += 1

_, all_directors, _ = call("GET", "/api/directors")
removed_directors = 0
if isinstance(all_directors, list):
    for a in all_directors:
        if a["fullName"].startswith("Автотест Режисеренко"):
            call("DELETE", f"/api/directors/{a['id']}")
            removed_directors += 1

if removed_movies or removed_directors:
    print(f"  [..]   прибрано залишки минулих прогонів: "
          f"{removed_movies} фільмів(и), {removed_directors} режисер(ів)")

st, directors, _ = call("GET", "/api/directors")
check("статичний сідінг (HasData): 6 режисерів",
      st == 200 and isinstance(directors, list) and len(directors) == 6,
      f"status={st} count={len(directors) if isinstance(directors, list) else '?'}")

st, genres, _ = call("GET", "/api/genres")
check("статичний сідінг (HasData): 5 жанрів",
      st == 200 and isinstance(genres, list) and len(genres) == 5,
      f"status={st} count={len(genres) if isinstance(genres, list) else '?'}")

st, page, _ = call("GET", "/api/movies?page=1&pageSize=100")
seeded_total = page.get("totalCount") if isinstance(page, dict) else -1
seeded_barcodes = {b["barcode"] for b in page.get("items", [])} if isinstance(page, dict) else set()
expected_barcodes = {
    "482-001-00-0001-5", "482-001-00-0002-3", "482-001-00-0003-1",
    "482-001-00-0004-9", "482-001-00-0005-7", "482-001-00-0006-5",
}
check("програмний сідінг (DbInitializer): усі 6 фільмів на місці",
      st == 200 and expected_barcodes <= seeded_barcodes,
      f"status={st} missing={sorted(expected_barcodes - seeded_barcodes)}")

check("кирилиця в даних збереглася",
      isinstance(page, dict) and any("Тіні забутих предків" in b["title"] for b in page.get("items", [])),
      f"titles={[b['title'] for b in page.get('items', [])] if isinstance(page, dict) else '?'}")

# =============================================================================
section("1. ПАГІНАЦІЯ: totalCount — це ВСІ записи, а не кількість на сторінці")
# =============================================================================
st, p2, headers = call("GET", "/api/movies?page=2&pageSize=5")
check("GET /api/movies?page=2&pageSize=5 -> 200", st == 200, f"status={st}")
check("totalCount дорівнює загальній кількості фільмів у БД (6), а не 1",
      isinstance(p2, dict) and p2.get("totalCount") == seeded_total,
      f"totalCount={p2.get('totalCount') if isinstance(p2, dict) else '?'} expected={seeded_total}")
expected_on_page2 = max(0, min(5, seeded_total - 5))
check(f"на другій сторінці рівно {expected_on_page2} запис(ів) ({seeded_total} фільмів / 5 на хвилину)",
      isinstance(p2, dict) and len(p2.get("items", [])) == expected_on_page2,
      f"items={len(p2.get('items', [])) if isinstance(p2, dict) else '?'}")
check("метадані пагінації присутні",
      isinstance(p2, dict) and all(k in p2 for k in ("page", "pageSize", "totalPages", "hasNext", "hasPrevious")),
      f"keys={list(p2.keys()) if isinstance(p2, dict) else '?'}")
expected_has_next = seeded_total > 10
check(f"hasPrevious=True, hasNext={expected_has_next}",
      isinstance(p2, dict) and p2.get("hasPrevious") is True
      and p2.get("hasNext") is expected_has_next,
      f"hasPrevious={p2.get('hasPrevious')} hasNext={p2.get('hasNext')}")
check("метадані продубльовано в заголовках X-Pagination-*",
      headers.get("X-Pagination-TotalCount") == str(seeded_total),
      f"header={headers.get('X-Pagination-TotalCount')}")

st, p1, _ = call("GET", "/api/movies?page=1&pageSize=5")
ids_p1 = {b["id"] for b in p1.get("items", [])} if isinstance(p1, dict) else set()
ids_p2 = {b["id"] for b in p2.get("items", [])} if isinstance(p2, dict) else set()
check("хвилини не перетинаються (Skip/Take виконує БД)",
      len(ids_p1 & ids_p2) == 0 and len(ids_p1) == 5,
      f"p1={sorted(ids_p1)} p2={sorted(ids_p2)}")

# =============================================================================
section("2. ФІЛЬТРАЦІЯ та СОРТУВАННЯ на рівні БД")
# =============================================================================
st, r, _ = call("GET", "/api/movies?search=Тіні забутих предків")
check("фільтр ?search= (кирилиця) повертає 1 фільм",
      st == 200 and r.get("totalCount") == 1,
      f"status={st} totalCount={r.get('totalCount') if isinstance(r, dict) else r}")

st, r, _ = call("GET", "/api/movies?minRate=5")
check("фільтр ?minRate=5 звужує вибірку",
      st == 200 and 0 < r.get("totalCount", 99) < seeded_total,
      f"totalCount={r.get('totalCount') if isinstance(r, dict) else '?'}")

st, r, _ = call("GET", "/api/movies?minRate=4&maxRate=5")
fees = [b["dailyRate"] for b in r.get("items", [])] if isinstance(r, dict) else []
check("діапазон ?minRate=4&maxRate=5 дотримано",
      st == 200 and all(4 <= f <= 5 for f in fees) and fees,
      f"fees={fees}")

st, asc, _ = call("GET", "/api/movies?sortBy=fee&sortDir=asc&pageSize=100")
st, desc, _ = call("GET", "/api/movies?sortBy=fee&sortDir=desc&pageSize=100")
fees_asc = [b["dailyRate"] for b in asc.get("items", [])] if isinstance(asc, dict) else []
fees_desc = [b["dailyRate"] for b in desc.get("items", [])] if isinstance(desc, dict) else []
check("сортування за тарифом за зростанням", fees_asc == sorted(fees_asc), f"{fees_asc}")
check("сортування за тарифом за спаданням", fees_desc == sorted(fees_desc, reverse=True), f"{fees_desc}")

st, r, _ = call("GET", "/api/movies?genre=Комедія")
check("фільтр за жанром через зв'язок M:N",
      st == 200 and r.get("totalCount", 0) >= 1,
      f"totalCount={r.get('totalCount') if isinstance(r, dict) else '?'}")

st, r, _ = call("GET", "/api/movies?directorId=1")
director1_movies = r.get("items", []) if isinstance(r, dict) else []
check("фільтр за режисером повертає лише фільми цього режисера",
      st == 200 and director1_movies and all(b["directorId"] == 1 for b in director1_movies),
      f"directorIds={[b['directorId'] for b in director1_movies]}")

# =============================================================================
section("3. ВАЛІДАЦІЯ: 400 з переліком помилок по полях, а не 500")
# =============================================================================
st, body, _ = call("POST", "/api/movies", {
    "title": "", "barcode": "bad-barcode", "dailyRate": 0, "directorId": 0
})
check("POST з порожнім Name -> 400 (а не 500)", st == 400, f"status={st}")
errors = body.get("errors") if isinstance(body, dict) else None
check("помилки розбиті по полях",
      isinstance(errors, dict) and len(errors) >= 3,
      f"errors={errors}")
check("формат ValidationProblemDetails",
      isinstance(body, dict) and body.get("title") and "status" in body,
      f"body={body}")

st, body, _ = call("POST", "/api/movies", {
    "title": "Тест формату Barcode", "barcode": "1234567890", "dailyRate": 5, "directorId": 1
})
check("FluentValidation: некоректний формат Barcode -> 400", st == 400, f"status={st}")
check("повідомлення валідатора українською",
      isinstance(body, dict) and "Barcode" in str(body.get("errors", {})),
      f"errors={body.get('errors') if isinstance(body, dict) else body}")

st, body, _ = call("POST", "/api/movies", {
    "title": "Взаємозалежні поля", "barcode": f"482-001-00-{SUFFIX}-1",
    "dailyRate": 5, "directorId": 1, "studio": "Студія без опису"
})
check("FluentValidation: studio без description -> 400", st == 400, f"status={st}")

st, body, _ = call("GET", "/api/movies?sortBy=drop_table")
check("недозволене поле сортування -> 400", st == 400, f"status={st}")

st, body, _ = call("GET", "/api/movies?pageSize=5000")
check("завеликий pageSize -> 400", st == 400, f"status={st}")

st, body, _ = call("GET", "/api/movies?minRate=50&maxRate=10")
check("maxRate < minRate -> 400", st == 400, f"status={st}")

# =============================================================================
section("4. 404 у форматі ProblemDetails, а не 500")
# =============================================================================
st, body, _ = call("GET", "/api/movies/999999")
check("GET /api/movies/999999 -> 404", st == 404, f"status={st}")
check("формат ProblemDetails",
      isinstance(body, dict) and {"type", "title", "status"} <= set(body),
      f"body={body}")

st, body, _ = call("GET", "/api/directors/999999")
check("GET /api/directors/999999 -> 404", st == 404, f"status={st}")

st, body, _ = call("POST", "/api/movies/999999/add-stock", {"quantity": 1})
check("операція над неіснуючим фільмом -> 404", st == 404, f"status={st}")

# =============================================================================
section("5. КОНФЛІКТ 409: повторний Barcode та інваріанти сутності")
# =============================================================================
barcode = f"482-001-00-{SUFFIX}-7"
st, created, headers = call("POST", "/api/movies", {
    "title": f"Автотест {SUFFIX}", "barcode": barcode, "dailyRate": 7.25,
    "directorId": 1, "releaseYear": 2020,
    "description": "Фільм, створена автотестом Лаб. 03.",
    "studio": "Тестова студія", "durationMinutes": 123,
    "genreIds": [3, 4],
})
check("POST /api/movies -> 201 Created", st == 201, f"status={st} body={created}")
check("є заголовок Location", "Location" in headers, f"headers={list(headers.keys())}")
new_id = created.get("id") if isinstance(created, dict) else None
check("жанри призначено через зв'язок M:N",
      isinstance(created, dict) and len(created.get("genres", [])) == 2,
      f"genres={created.get('genres') if isinstance(created, dict) else '?'}")
check("опис створено (зв'язок 1:1)",
      isinstance(created, dict) and created.get("detail") is not None,
      f"detail={created.get('detail') if isinstance(created, dict) else '?'}")

st, body, _ = call("POST", "/api/movies", {
    "title": "Дублікат Barcode", "barcode": barcode, "dailyRate": 3,
    "directorId": 1, "description": "дублікат", "studio": "x", "durationMinutes": 10,
})
check("ПОВТОРНЕ створення з тим самим Barcode -> 409", st == 409, f"status={st}")
check("повідомлення пояснює конфлікт",
      isinstance(body, dict) and "Barcode" in str(body.get("detail", "")),
      f"detail={body.get('detail') if isinstance(body, dict) else body}")

st, body, _ = call("POST", "/api/movies", {
    "title": "Неіснуючий режисер", "barcode": f"482-001-00-{SUFFIX}-8",
    "dailyRate": 3, "directorId": 9999,
})
check("створення з неіснуючим режисером -> 404", st == 404, f"status={st}")

# =============================================================================
section("6. ІНВАРІАНТИ СУТНОСТІ (бізнес-правила в методах, а не в setter-ах)")
# =============================================================================
st, movie, _ = call("GET", f"/api/movies/{new_id}")
discs_before = movie.get("availableDiscs", 0) if isinstance(movie, dict) else 0
check("новий фільм без дисків має availableDiscs = 0", discs_before == 0,
      f"availableDiscs={discs_before}")

st, body, _ = call("POST", f"/api/movies/{new_id}/remove-stock", {"quantity": 5})
check("Movie.RemoveStock при нульовому залишку -> 409", st == 409, f"status={st}")
check("повідомлення — з методу сутності",
      isinstance(body, dict) and "available" in str(body.get("detail", "")).lower(),
      f"detail={body.get('detail') if isinstance(body, dict) else body}")

st, body, _ = call("POST", f"/api/movies/{new_id}/discs", {
    "discNumber": f"AT-{SUFFIX}-1", "shelf": "Z-01", "condition": "New"
})
check("додавання диска -> 200 і +1 до залишку",
      st == 200 and body.get("availableDiscs") == 1,
      f"status={st} availableDiscs={body.get('availableDiscs') if isinstance(body, dict) else '?'}")
check("диск зʼявився у списку (зв'язок 1:N)",
      isinstance(body, dict) and len(body.get("discs", [])) == 1,
      f"discs={body.get('discs') if isinstance(body, dict) else '?'}")

st, body, _ = call("POST", f"/api/movies/{new_id}/discs", {
    "discNumber": f"AT-{SUFFIX}-1", "shelf": "Z-02", "condition": "Good"
})
check("повторний інвентарний номер -> 409 (інваріант сутності)", st == 409, f"status={st}")

st, body, _ = call("POST", f"/api/movies/{new_id}/discs", {
    "discNumber": "нелатиниця", "condition": "Good"
})
check("інвентарний номер не за шаблоном -> 400 (FluentValidation)", st == 400, f"status={st}")

st, body, _ = call("POST", f"/api/movies/{new_id}/discs", {
    "discNumber": f"AT-{SUFFIX}-2", "condition": "Броньований"
})
check("невідомий стан диска -> 400", st == 400, f"status={st}")

st, body, _ = call("PUT", f"/api/movies/{new_id}", {"title": "Перейменовано", "dailyRate": 0})
check("тариф 0 -> 400 (DataAnnotations/FluentValidation)", st == 400, f"status={st}")

st, body, _ = call("POST", f"/api/movies/{new_id}/genres", {"genreId": 3, "isPrimary": False})
check("повторне призначення того самого жанру -> 409", st == 409, f"status={st}")

st, body, _ = call("POST", f"/api/movies/{new_id}/genres", {"genreId": 9999})
check("призначення неіснуючого жанру -> 404", st == 404, f"status={st}")

# =============================================================================
section("7. EAGER vs EXPLICIT LOADING")
# =============================================================================
st, movie, _ = call("GET", "/api/movies/1")
check("Eager Loading: режисер, опис, диски та жанри — в одній відповіді",
      st == 200 and movie.get("directorName") and movie.get("detail")
      and len(movie.get("discs", [])) >= 1 and len(movie.get("genres", [])) >= 1,
      f"directorName={movie.get('directorName')} detail={bool(movie.get('detail'))} "
      f"discs={len(movie.get('discs', []))} genres={movie.get('genres')}")

st, a_no, _ = call("GET", "/api/directors/1?includeMovies=false")
check("Explicit Loading вимкнено: фільми не завантажено",
      st == 200 and len(a_no.get("movies", [])) == 0,
      f"movies={len(a_no.get('movies', [])) if isinstance(a_no, dict) else '?'}")

st, a_yes, _ = call("GET", "/api/directors/1?includeMovies=true")
check("Explicit Loading увімкнено: фільми довантажено ДРУГИМ запитом",
      st == 200 and len(a_yes.get("movies", [])) >= 1,
      f"movies={len(a_yes.get('movies', [])) if isinstance(a_yes, dict) else '?'}")

# =============================================================================
section("8. LINQ через проміжну сутність M:N (власне поле AddedAt)")
# =============================================================================
st, recent, _ = call("GET", "/api/genres/Комедія/recent-movies?since=2000-01-01T00:00:00Z")
check("GET /api/genres/{name}/recent-movies -> 200",
      st == 200 and isinstance(recent, list), f"status={st} body={recent}")
check("повернуто фільми жанру «Комедія»",
      isinstance(recent, list) and len(recent) >= 1,
      f"count={len(recent) if isinstance(recent, list) else '?'}")

st, future, _ = call("GET", "/api/genres/Комедія/recent-movies?since=2099-01-01T00:00:00Z")
check("фільтр за AddedAt працює: з майбутньої дати — порожньо",
      st == 200 and isinstance(future, list) and len(future) == 0,
      f"count={len(future) if isinstance(future, list) else '?'}")

st, stats, _ = call("GET", "/api/genres/stats")
check("GET /api/genres/stats -> 200 (GROUP BY виконує БД)",
      st == 200 and isinstance(stats, list) and stats, f"status={st}")
check("у статистиці є назва жанру й кількість фільмів",
      isinstance(stats, list) and stats and "genreName" in stats[0] and "movieCount" in stats[0],
      f"row={stats[0] if isinstance(stats, list) and stats else '?'}")

# =============================================================================
section("9. Друга й третя специфікації через той самий WithSpecification()")
# =============================================================================
st, oos, _ = call("GET", "/api/movies/out-of-stock")
check("GET /api/movies/out-of-stock -> 200 (друга специфікація)",
      st == 200 and isinstance(oos, list), f"status={st}")
check("новий фільм без дисків більше не в списку (їй додали диск)",
      isinstance(oos, list) and all(b["id"] != new_id for b in oos),
      f"ids={[b['id'] for b in oos] if isinstance(oos, list) else '?'}")

st, by_genre, _ = call("GET", "/api/movies/by-genre/comedy?take=5")
check("GET /api/movies/by-genre/comedy -> 200 (третя специфікація)",
      st == 200 and isinstance(by_genre, list) and len(by_genre) >= 1,
      f"status={st} count={len(by_genre) if isinstance(by_genre, list) else '?'}")

st, empty, _ = call("GET", "/api/movies/by-genre/no-such-slug")
check("неіснуючий slug -> 200 і порожній список",
      st == 200 and isinstance(empty, list) and len(empty) == 0,
      f"status={st} count={len(empty) if isinstance(empty, list) else '?'}")

# =============================================================================
section("10. Режисери: створення, конфлікт, інваріант видалення")
# =============================================================================
st, director, _ = call("POST", "/api/directors", {
    "fullName": f"Автотест Режисеренко {SUFFIX}", "country": "Україна", "birthYear": 1980
})
check("POST /api/directors -> 201 Created", st == 201, f"status={st} body={director}")
director_id = director.get("id") if isinstance(director, dict) else None

st, body, _ = call("POST", "/api/directors", {"fullName": f"Автотест Режисеренко {SUFFIX}"})
check("повторне імʼя режисера -> 409", st == 409, f"status={st}")

st, body, _ = call("DELETE", f"/api/directors/{director_id}")
check("видалення режисера БЕЗ фільмів -> 204", st == 204, f"status={st}")

st, body, _ = call("DELETE", "/api/directors/1")
check("видалення режисера З фільмами -> 409 (інваріант сутності)", st == 409, f"status={st}")
check("повідомлення пояснює причину",
      isinstance(body, dict) and "movie" in str(body.get("detail", "")).lower(),
      f"detail={body.get('detail') if isinstance(body, dict) else body}")

# =============================================================================
section("11. М'яке видалення фільму")
# =============================================================================
st, _, _ = call("DELETE", f"/api/movies/{new_id}")
check("DELETE /api/movies/{id} -> 204", st == 204, f"status={st}")

st, body, _ = call("GET", f"/api/movies/{new_id}")
check("видалений фільм більше не читається -> 404", st == 404, f"status={st}")

st, page_after, _ = call("GET", "/api/movies?pageSize=100")
check("видалений фільм зникла зі списку",
      isinstance(page_after, dict) and all(b["id"] != new_id for b in page_after.get("items", [])),
      f"ids={[b['id'] for b in page_after.get('items', [])] if isinstance(page_after, dict) else '?'}")
check("інші фільми на місці (видалення саме м'яке, не каскадне)",
      isinstance(page_after, dict) and page_after.get("totalCount") == seeded_total,
      f"totalCount={page_after.get('totalCount') if isinstance(page_after, dict) else '?'}")

# =============================================================================
section("12. Swagger / OpenAPI")
# =============================================================================
st, spec, _ = call("GET", "/swagger/v1/swagger.json")
check("GET /swagger/v1/swagger.json -> 200", st == 200, f"status={st}")
check("описано всі три групи ендпоінтів",
      isinstance(spec, dict) and all(
          any(p.startswith(pref) for p in spec.get("paths", {}))
          for pref in ("/api/movies", "/api/directors", "/api/genres")),
      f"paths={list(spec.get('paths', {}).keys()) if isinstance(spec, dict) else '?'}")

# =============================================================================
print()
print("=" * 72)
print(f" РЕЗУЛЬТАТ: {len(passed)} пройдено, {len(failed)} провалено")
print("=" * 72)
if failed:
    print("\nПровалені перевірки:")
    for f in failed:
        print(f"  - {f}")
    sys.exit(1)
print("\nУсі критерії приймання Лабораторної роботи № 03 виконано.")
sys.exit(0)
