# Yuru-Chara Map

An interactive map of Japan's 47 prefectures and their official mascots. Select a
prefecture on the map or in the list to see its mascot: the name in kana and romaji, the
motif, the debut year, the owning body, the official site, and the source of each fact.
The data has 35 mascots. 14 prefectures have no mascot that a source could verify, and
the map colours them differently from the prefectures that have one.

**Live site:** <https://yuru-chara.vercel.app/>. The first visit of the day can take up to
about 15 seconds, because the API host stops the application when nobody uses it.

The app shows no mascot images. The prefectures own the mascot designs, so each mascot
record stores what its owner's image terms permit, and the detail panel shows that
statement where an image would be.

## Documents

- [CLAUDE.md](CLAUDE.md) is the project brief: scope, stack, domain model, API, spatial
  design, conventions and commands. It is written as instructions to the coding agent
  used on this project, which is why it uses the imperative.
- [DECISIONS.md](DECISIONS.md) records each design decision, the alternative that was
  rejected, the reason, and the cost.
- [DATA-SOURCES.md](DATA-SOURCES.md) records the source, licence and retrieval date of
  each dataset.

## Run it locally

You need the .NET SDK 10.0.302, Docker with Compose, OpenSSL, and Node.js 20.19 or later.

```bash
./scripts/dev-setup.sh
docker compose up -d
dotnet tool restore
dotnet ef database update --project src/YuruChara.Infrastructure
dotnet run --project src/YuruChara.Ingestion -- seed
dotnet run --project src/YuruChara.Api
cd web && npm install && npm run dev    # in a second terminal
```

`scripts/dev-setup.sh` generates a random database password and stores it in the .NET
user-secrets store, which is outside the repository. It then writes `.env` for Docker
Compose from that stored value, so the password exists in one place only
([DECISIONS.md entry 11](DECISIONS.md)). To use a password of your own, set it with
`dotnet user-secrets set "ConnectionStrings:YuruChara" "<connection string>" --project src/YuruChara.Api`,
then run the script again. The seed command loads the committed data in `data/` and is
safe to run more than once. `dotnet test` also needs Docker, because the tests start
their own PostgreSQL container.

## Boundary data

The prefecture boundaries are derived from Japanese government data. Its licence requires
this statement:

> 「国土数値情報（行政区域データ）」（国土交通省）
> <https://nlftp.mlit.go.jp/ksj/gml/datalist/KsjTmplt-N03-2024.html>
> をもとにスマートニュース メディア研究所および本プロジェクトが作成（境界線を簡素化）

Contains information from the National Land Numerical Information (Administrative
Divisions) published by the Ministry of Land, Infrastructure, Transport and Tourism of
Japan, processed by SmartNews Media Research Institute and by this project (boundaries
simplified). Not produced by, or endorsed by, the Government of Japan.
