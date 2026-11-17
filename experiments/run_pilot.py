"""Paired technical pilot. Default: show plan without requests or charges."""
import argparse
import getpass
import json
import os
import pathlib
import time
import urllib.error
import urllib.request
import uuid


def main():
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument("--execute", action="store_true", help="Create private materials and make paid AI requests")
    parser.add_argument("--base-url", default="http://localhost:5080")
    parser.add_argument("--origin", default="http://localhost:3000")
    parser.add_argument("--case", help="Select one case from fixtures/cases.json")
    parser.add_argument("--repeats", type=int, default=1, choices=range(1, 4))
    parser.add_argument("--series", default="EduForge technical pilot")
    args = parser.parse_args()
    folder = pathlib.Path(__file__).parent / "fixtures"
    cases = json.loads((folder / "cases.json").read_text(encoding="utf-8-sig"))["cases"]
    if args.case:
        cases = [case for case in cases if case["id"] == args.case]
        if not cases:
            parser.error("Unknown case")
    planned = len(cases) * args.repeats * 2
    print(f"Cases: {len(cases)}; repeats: {args.repeats}; AI requests: {planned}; 4 questions per request.")
    if not args.execute:
        print("Plan only. --execute enables API requests. Credentials: EDUFORGE_PILOT_EMAIL/PASSWORD or interactive prompts.")
        return
    email = os.environ.get("EDUFORGE_PILOT_EMAIL") or input("Teacher email: ")
    password = os.environ.get("EDUFORGE_PILOT_PASSWORD") or getpass.getpass("Teacher password: ")
    cookie = ""

    def request(path, payload=None, method=None):
        data = None if payload is None else json.dumps(payload, ensure_ascii=False).encode()
        headers = {"Origin": args.origin, "Content-Type": "application/json"}
        if cookie:
            headers["Cookie"] = cookie
        for attempt in range(3):
            req = urllib.request.Request(args.base_url.rstrip("/") + path, data=data, headers=headers,
                                         method=method or ("POST" if data is not None else "GET"))
            try:
                with urllib.request.urlopen(req, timeout=150) as response:
                    body = response.read()
                    return (json.loads(body) if body else None), response.headers
            except urllib.error.HTTPError as error:
                if error.code == 429 and attempt < 2:
                    print("Rate limit: retrying after one minute.", flush=True)
                    time.sleep(60)
                    continue
                try:
                    detail = json.load(error)
                    message = detail.get("detail") or detail.get("message") or "Request failed"
                except (ValueError, AttributeError):
                    message = "Request failed"
                raise RuntimeError(f"{path}: HTTP {error.code}: {message}") from None

    _, headers = request("/api/auth/login", {"email": email, "password": password})
    cookie = headers.get("Set-Cookie").split(";", 1)[0]
    print("Authenticated. No credentials are written to output.", flush=True)
    for case_index, case in enumerate(cases):
        text = (folder / case["file"]).read_text(encoding="utf-8-sig")
        material, _ = request("/api/materials", {"title": f"{args.series}: {case['id']}", "subject": case["subject"], "content": text})
        source, _ = request(f"/api/materials/{material['id']}/learning")
        source, _ = request(f"/api/materials/{material['id']}/learning", {"revisionId": source["revisionId"], "planVersion": source["planVersion"],
            "objectives": [{"id": str(uuid.uuid4()), "text": goal} for goal in case["objectives"]]}, "PUT")
        parameters = {"materialId": material["id"], "questionCount": 4, "difficulty": "Лёгкая", "types": ["SingleChoice"], "totalPoints": 20}
        for repeat in range(args.repeats):
            # Alternate presentation order to avoid always testing the same mode first.
            for controlled in ([False, True] if (case_index + repeat) % 2 == 0 else [True, False]):
                mode = "controlled" if controlled else "ordinary"
                path = "/api/ai/test/controlled" if controlled else "/api/ai/test"
                payload = {"quiz": parameters, "revisionId": source["revisionId"], "planVersion": source["planVersion"]} if controlled else parameters
                generated, _ = request(path, payload)
                generated["title"] = f"{args.series}: {case['id']} {mode} #{repeat + 1}"
                saved, _ = request("/api/tests", generated)
                request(f"/api/generations/{generated['generationRunId']}/evaluation", {"experiment": args.series,
                    "preparationMinutes": None, "correctness": None, "clarity": None, "alignment": None, "correctedQuestions": None,
                    "note": f"{case['id']}; repetition {repeat + 1}; expert review pending"}, "PUT")
                print(f"Saved {case['id']} / {mode} / repetition {repeat + 1}; questions: {len(saved['questions'])}", flush=True)
    print("Pilot outputs saved privately. Review them under Tests and Generation history. Expert scores remain empty.")


if __name__ == "__main__":
    try:
        main()
    except (RuntimeError, urllib.error.URLError) as error:
        raise SystemExit(str(error))
