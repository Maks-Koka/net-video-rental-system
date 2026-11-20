"""
Наскрізні перевірки мікросервісної платформи (Лаб. 05).

Запросити платформу треба заздалегідь:

    dotnet run --project src/VideoRental.Platform.AppHost --launch-profile http

Усі звернення йдуть ЛИШЕ через Gateway (порт 5000) — так перевіряється і
маршрутизація YARP, і service discovery, і наскрізний CorrelationId.

    python tests/api-tests.py

Сценарій часткової відмови (зупинка reviews-api) перевіряється окремо:

    python tests/api-tests.py --partial-failure
"""

import json
import sys
import time
import urllib.error
import urllib.request

GATEWAY = "http://localhost:5001"

passed = 0
failed = 0
failures: list[str] = []


def check(name: str, condition: bool, detail: str = "") -> bool:
    global passed, failed
    if condition:
        passed += 1
        print(f"  OK   {name}")
    else:
        failed += 1
        failures.append(f"{name}: {detail}")
        print(f"  FAIL {name}  {detail}")
    return condition


def decode(raw: bytes):
    """JSON, якщо це JSON; інакше — текст (health-ендпоінти віддають text/plain)."""
    if not raw:
        return None
    try:
        return json.loads(raw)
    except json.JSONDecodeError:
        return raw.decode(errors="replace")


def request(method: str, path: str, body=None, headers=None):
    """Повертає (статус, заголовки, розібране тіло)."""
    data = json.dumps(body).encode() if body is not None else None
    req = urllib.request.Request(GATEWAY + path, data=data, method=method)
    req.add_header("Accept", "application/json")
    if data:
        req.add_header("Content-Type", "application/json")
    for key, value in (headers or {}).items():
        req.add_header(key, value)

    try:
        with urllib.request.urlopen(req, timeout=60) as response:
            return response.status, dict(response.headers), decode(response.read())
    except urllib.error.HTTPError as error:
        return error.code, dict(error.headers), decode(error.read())


def wait_for_gateway(attempts: int = 60) -> bool:
    for _ in range(attempts):
        try:
            status, _, _ = request("GET", "/")
            if status == 200:
                return True
        except Exception:
            pass
        time.sleep(2)
    return False


# ==========================================================================
#  1. Маршрути Gateway
# ==========================================================================
def test_routing():
    print("\n[1] Маршрутизація YARP: один порт -> три мікросервіси")

    status, _, body = request("GET", "/")
    check("Gateway відповідає на корінь", status == 200, f"статус {status}")
    if isinstance(body, dict):
        routes = {r["route"] for r in body.get("routes", [])}
        check("усі маршрути зареєстровані",
              {"aggregator", "rentals", "movies", "reviews", "rentals-movies"} <= routes,
              str(sorted(routes)))
        addresses = [d for c in body.get("clusters", []) for d in (c.get("destinations") or [])]
        check("кластери використовують логічні імена, а не порти",
              all("localhost" not in a for a in addresses), str(addresses))

    status, _, rental = request("GET", "/api/rentals/1")
    check("/api/rentals/1 -> rentals-api", status == 200, f"статус {status}")
    check("оренда містить позиції", bool(rental and rental.get("items")))

    status, _, customers = request("GET", "/api/customers")
    check("/api/customers -> rentals-api", status == 200, f"статус {status}")

    status, _, projection = request("GET", "/api/rentals/movies/1")
    check("/api/rentals/movies/1 -> проєкція rentals-api (PathPattern)",
          status == 200, f"статус {status}")
    check("проєкція має час синхронізації",
          bool(projection and "syncedAt" in projection))

    status, _, movies = request("GET", "/api/movies?page=1&pageSize=3")
    check("/api/movies -> catalog-api", status == 200, f"статус {status}")
    check("каталог повернув хвилину", bool(movies and movies.get("items")))

    status, _, _ = request("GET", "/api/directors")
    check("/api/directors -> catalog-api", status == 200, f"статус {status}")

    status, _, _ = request("GET", "/api/genres")
    check("/api/genres -> catalog-api", status == 200, f"статус {status}")

    status, _, reviews = request("GET", "/api/reviews?movieId=1")
    check("/api/reviews -> reviews-api", status == 200, f"статус {status}")
    check("відгуки повернули хвилину", bool(reviews and "items" in reviews))

    status, _, ratings = request("GET", "/api/reviews/ratings")
    check("/api/reviews/ratings -> aggregation pipeline MongoDB",
          status == 200 and isinstance(ratings, list), f"статус {status}")

    status, _, _ = request("GET", "/api/unknown/42")
    check("невідомий шлях -> 404 від шлюзу", status == 404, f"статус {status}")


# ==========================================================================
#  2. CorrelationId
# ==========================================================================
def test_correlation_id():
    print("\n[2] Наскрізний CorrelationId")

    status, headers, _ = request("GET", "/api/rentals/1")
    value = headers.get("X-Correlation-Id")
    check("Gateway додає X-Correlation-Id у відповідь", bool(value), str(headers))

    if value:
        check("значення — GUID", len(value) == 36 and value.count("-") == 4, value)

    # той самий заголовок, але не два однакових значення
    check("заголовок не продубльований", value is None or "," not in value, str(value))

    mine = "11111111-2222-3333-4444-555555555555"
    status, headers, _ = request("GET", "/api/rentals/1",
                                 headers={"X-Correlation-Id": mine})
    check("власний ідентифікатор клієнта зберігається",
          headers.get("X-Correlation-Id") == mine, str(headers.get("X-Correlation-Id")))

    # сервіс поза шлюзом теж має створити ідентифікатор — перевіряється
    # на агрегувальному ендпоінті, який проходить два компоненти
    status, headers, _ = request("GET", "/api/aggregator/movies/1/card")
    check("агрегувальний запит теж має CorrelationId",
          bool(headers.get("X-Correlation-Id")), str(headers))


# ==========================================================================
#  3. Агрегатор
# ==========================================================================
def test_aggregator():
    print("\n[3] Aggregator Pattern: паралельні виклики й композиція")

    status, _, full = request("GET", "/api/aggregator/rentals/1/full")
    check("GET /api/aggregator/rentals/1/full -> 200", status == 200, f"статус {status}")

    if status == 200 and isinstance(full, dict):
        check("є блок оренди", full.get("rental", {}).get("id") == 1)
        check("є дані клієнта", bool(full.get("customer", {}).get("fullName")))
        check("є фільми з Каталогу", len(full.get("movies") or []) > 0)
        check("є блок meta", "meta" in full)

        meta = full.get("meta", {})
        check("meta містить запис про кожен виклик",
              len(meta.get("calls") or []) == 5, str(meta.get("calls")))
        check("паралельність дала виграш у часі",
              meta.get("sequentialMs", 0) >= meta.get("totalMs", 0),
              f"total={meta.get('totalMs')} sequential={meta.get('sequentialMs')}")
        print(f"       total={meta.get('totalMs')} мс, "
              f"sequential={meta.get('sequentialMs')} мс, "
              f"saved={meta.get('savedByParallelismMs')} мс")

        movie_ids = {item["movieId"] for item in full["rental"]["items"]}
        check("у відповіді лише фільми цієї оренди",
              all(b["id"] in movie_ids for b in full.get("movies") or []))
        check("у відповіді лише відгуки на фільми цієї оренди",
              all(r["movieId"] in movie_ids for r in full.get("reviews") or []))

    status, _, card = request("GET", "/api/aggregator/movies/1/card")
    check("GET /api/aggregator/movies/1/card -> 200", status == 200, f"статус {status}")
    if status == 200 and isinstance(card, dict):
        check("картка містить дані Каталогу", bool(card.get("movie", {}).get("title")))
        check("картка містить проєкцію Сервісу оренд",
              "rentalsProjection" in card, str(list(card.keys())))

    status, _, problem = request("GET", "/api/aggregator/rentals/999999/full")
    check("неіснуюча оренда -> 404 ProblemDetails", status == 404, f"статус {status}")
    check("404 містить пояснення",
          isinstance(problem, dict) and "title" in problem, str(problem))


# ==========================================================================
#  4. Операції запису через Gateway
# ==========================================================================
def test_writes():
    print("\n[4] Операції запису через Gateway")

    number = f"VR-{int(time.time()) % 100000:05d}"
    status, _, customer = request("POST", "/api/customers", {
        "membershipNumber": number,
        "email": f"{number.lower()}@videorental.test",
        "fullName": "Платформний Тест",
        "phone": "+380991234567",
    })
    check("POST /api/customers -> 201", status == 201, f"статус {status} {customer}")

    if status != 201 or not isinstance(customer, dict):
        return

    customer_id = customer["id"]

    # прапорець передається у query string (Лаб. 02)
    status, _, _ = request("PUT", f"/api/customers/{customer_id}/blocked?value=true")
    check("PUT /api/customers/{id}/blocked -> 204", status == 204, f"статус {status}")

    status, _, blocked = request("GET", f"/api/customers/{customer_id}")
    check("зміну видно наступним GET", bool(blocked and blocked.get("isBlocked")))

    status, _, _ = request("DELETE", f"/api/customers/{customer_id}")
    check("DELETE /api/customers/{id} -> 204", status == 204, f"статус {status}")

    status, _, _ = request("GET", f"/api/customers/{customer_id}")
    check("видаленого клієнта більше немає", status == 404, f"статус {status}")

    status, _, problem = request("POST", "/api/customers", {"membershipNumber": "x"})
    check("некоректні дані -> 400 ValidationProblemDetails",
          status == 400, f"статус {status}")
    check("400 містить перелік помилок",
          isinstance(problem, dict) and "errors" in problem, str(problem))


# ==========================================================================
#  5. Health checks
# ==========================================================================
def test_health():
    print("\n[5] Health checks")

    status, _, body = request("GET", "/health")
    check("Gateway /health -> Healthy", status == 200 and body == "Healthy",
          f"статус {status}, тіло {body}")

    status, _, body = request("GET", "/alive")
    check("Gateway /alive -> Healthy", status == 200 and body == "Healthy",
          f"статус {status}, тіло {body}")


# ==========================================================================
#  6. Часткова відмова (окремий запуск)
# ==========================================================================
def test_partial_failure():
    print("\n[*] Часткова відмова: reviews-api має бути ЗУПИНЕНИЙ у Dashboard")

    status, _, full = request("GET", "/api/aggregator/rentals/1/full")
    check("агрегувальний запит НЕ падає з 500", status == 200, f"статус {status}")

    if status == 200 and isinstance(full, dict):
        check("оренда на місці", bool(full.get("rental")))
        check("фільми Каталогу на місці", len(full.get("movies") or []) > 0)
        check("відгуків немає", not full.get("reviews"))
        warnings = full.get("warnings") or []
        check("у warnings є причина",
              any("reviews-api" in w for w in warnings), str(warnings))
        for warning in warnings:
            print(f"       warning: {warning}")
        check("meta позначила виклик як unavailable",
              any(c["status"] == "unavailable" for c in full.get("meta", {}).get("calls", [])))


def main():
    partial = "--partial-failure" in sys.argv

    print(f"Платформа: {GATEWAY}")
    if not wait_for_gateway():
        print("Gateway не відповідає — платформа не запущена?")
        return 1

    if partial:
        test_partial_failure()
    else:
        test_routing()
        test_correlation_id()
        test_aggregator()
        test_writes()
        test_health()

    print(f"\n{'=' * 60}")
    print(f"Пройдено: {passed}   Не пройдено: {failed}")
    if failures:
        print("\nНе пройдено:")
        for failure in failures:
            print(f"  - {failure}")
    return 0 if failed == 0 else 1


if __name__ == "__main__":
    sys.exit(main())
