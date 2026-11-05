# Local API Contracts and Developer Diagnostics

This guide applies to the current technical API. No business endpoints or
application authentication are implemented. Run the examples from the repository
root on a trusted development machine. They use a loopback interface only.

## Development: OpenAPI and Scalar

Build the API after changing source code:

```bash
dotnet build src/Alycia.Api/Alycia.Api.csproj
```

Start it in **Development** in a dedicated terminal:

```bash
env ASPNETCORE_ENVIRONMENT=Development \
  dotnet run --no-build --no-launch-profile \
  --project src/Alycia.Api/Alycia.Api.csproj \
  -- --urls http://127.0.0.1:5187
```

In another terminal, check the existing health route and export generated OpenAPI:

```bash
curl -i http://127.0.0.1:5187/healthz
curl --fail-with-body -sS \
  -o /tmp/alycia-s5-openapi-v1.json \
  -w 'OpenAPI HTTP %{http_code}\n' \
  http://127.0.0.1:5187/openapi/v1.json
python3 -m json.tool /tmp/alycia-s5-openapi-v1.json > /dev/null
```

Open `http://127.0.0.1:5187/scalar/` in a browser. Scalar must load the
same generated `v1` document and display `GET /healthz`. The actual healthy
response is HTTP `200`, body `Healthy`, content type `text/plain`. The OpenAPI
contract also documents HTTP `503` for an unhealthy result. The technical health
route is outside the application JSON envelope and does not check PostgreSQL.

The OpenAPI `info.version` identifies the **API document**, not a published
Alycia software release. No Alycia version has been published yet.

Stop the local server with `Ctrl+C` when the check is complete.

## Production: documentation endpoints are absent

Launch the same API in **Production** on a separate local port:

```bash
env ASPNETCORE_ENVIRONMENT=Production \
  dotnet run --no-build --no-launch-profile \
  --project src/Alycia.Api/Alycia.Api.csproj \
  -- --urls http://127.0.0.1:5188
```

In another terminal:

```bash
curl -sS -o /dev/null -w '/healthz: HTTP %{http_code}\n' \
  http://127.0.0.1:5188/healthz
curl -sS -o /dev/null -w '/openapi/v1.json: HTTP %{http_code}\n' \
  http://127.0.0.1:5188/openapi/v1.json
curl -sS -o /dev/null -w '/scalar/: HTTP %{http_code}\n' \
  http://127.0.0.1:5188/scalar/
```

Expected statuses for a healthy API are `200`, `404`, and `404`, respectively.
The Docker Compose host binding remains `127.0.0.1:8080:8080`; do not change
the production environment to Development to expose documentation. Stop the
server with `Ctrl+C` after checking.

## Find a real request trace

The existing Serilog request-completion events contain `TraceId`,
`RequestMethod`, `RequestPath`, `StatusCode`, and `Elapsed`. For the direct
.NET launch above, JSON Lines files are stored under `src/Alycia.Api/logs/`
with the default configuration. Docker Compose stores the API logs inside its
persistent volume at `/app/logs/` instead.

After making an actual `GET /healthz` request, print the most recent matching
request's diagnostic metadata (not its full event payload):

```bash
python3 - <<'PY'
import json
from pathlib import Path

matches = []
for path in sorted(Path('src/Alycia.Api/logs').glob('alycia-*.jsonl')):
    with path.open(encoding='utf-8') as log:
        for line in log:
            if not line.strip():
                continue
            event = json.loads(line)
            if (event.get('RequestMethod') == 'GET'
                    and event.get('RequestPath') == '/healthz'
                    and event.get('TraceId')):
                matches.append((path, event))

if not matches:
    raise SystemExit('No correlated GET /healthz event found')

path, event = matches[-1]
print('Log:', path)
for field in ('@t', 'TraceId', 'RequestMethod', 'RequestPath',
              'StatusCode', 'Elapsed'):
    print(f'{field}: {event.get(field)}')
PY
```

The timestamp may be in UTC while console output uses local time. Copy the
**observed** trace identifier; do not invent a value.

## Export selected events as JSON Lines

Replace `ACTUAL_TRACE_ID` with a `TraceId` from the previous command.
For multiple selected health requests, separate their trace identifiers by
commas. The command uses Python's standard library only, keeps an allowlist
of diagnostic fields, writes outside the repository with restrictive file
permissions, and does not modify the source logs.

```bash
env ALYCIA_TRACE_IDS=ACTUAL_TRACE_ID python3 - <<'PY'
import json
import os
import tempfile
from pathlib import Path

trace_ids = {
    value.strip()
    for value in os.environ['ALYCIA_TRACE_IDS'].split(',')
    if value.strip()
}
if not trace_ids:
    raise SystemExit('No TraceId supplied')

fields = ('@t', 'TraceId', 'RequestMethod', 'RequestPath',
          'StatusCode', 'Elapsed')
count = 0
with tempfile.NamedTemporaryFile(
        mode='w', encoding='utf-8', dir='/tmp',
        prefix='alycia-s5-trace-', suffix='.jsonl',
        delete=False) as output:
    export_path = Path(output.name)
    for source in sorted(Path('src/Alycia.Api/logs').glob('alycia-*.jsonl')):
        with source.open(encoding='utf-8') as log:
            for line in log:
                if not line.strip():
                    continue
                event = json.loads(line)
                if (event.get('TraceId') not in trace_ids
                        or event.get('RequestMethod') != 'GET'
                        or event.get('RequestPath') != '/healthz'):
                    continue
                safe_event = {key: event[key] for key in fields if key in event}
                output.write(json.dumps(safe_event, ensure_ascii=False) + '\n')
                count += 1

if not count:
    export_path.unlink()
    raise SystemExit('No selected health events found')

with export_path.open(encoding='utf-8') as output:
    assert sum(1 for line in output if json.loads(line)) == count
print('Export:', export_path)
print('Events exported:', count)
print('Valid JSON Lines:', True)
print('Exported fields:', ', '.join(fields))
PY
```

The export remains JSON Lines (one JSON object per line), not a JSON array.
If a JSON array is required, convert an explicitly selected file with a JSON
parser; do not rename JSONL data to `.json` without converting its structure.

## Validation and data hygiene

From the repository root, use the existing local checks after relevant changes:

```bash
make check
git diff --check
```

The API build and Development/Production HTTP checks above complement these
checks. No CI/CD or additional observability service is required.

Never log or export request/response bodies, authorization headers, API keys,
prompts, document contents, or other sensitive values. Review a filtered
sample before sharing it. Keep temporary exports out of the repository and
remove them when no longer needed. The log-file rolling and retention settings
remain those described in `README.md`; this guide introduces no new capture
or persistence mechanism.
