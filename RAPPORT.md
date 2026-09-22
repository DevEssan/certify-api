# Teknisk leveransrapport

**Uppdrag:** Certify AB — Digitala certifikat med publik verifiering
**Konsultteam:** Essan Alshakerchi
**Datum:** 2026-09-20
**Version:** 1.0

## Sammanfattning

Vi har levererat en molnbaserad plattform för digitala certifikat. Kunden kan nu skapa ett certifikat via ett programmeringsgränssnitt (API) och få tillbaka en unik, offentligt verifierbar länk direkt — mottagaren eller vem som helst med länken kan bekräfta att certifikatet är äkta utan att kontakta utfärdaren. Plattformen är driftsatt i Microsoft Azure-molnet, körs alltid på minst två instanser, skalar automatiskt vid ökad belastning och uppdateras automatiskt vid varje kodändring. Ingen manuell hantering av Word-dokument eller e-post behövs längre för att utfärda eller verifiera ett certifikat.

## Vad som levereras

### Inkluderat i leveransen

| Komponent | Teknisk lösning | Status |
|---|---|---|
| REST API | .NET 10 WebAPI, 5 endpoints | ✅ Levererat |
| Containerisering | Docker, multi-stage build | ✅ Levererat |
| Driftsättning | Azure Container Apps, 2–3 repliker | ✅ Levererat |
| Bildarkiv | Azure Container Registry | ✅ Levererat |
| Fillagring | Azure Blob Storage | ✅ Levererat |
| Infrastruktur som kod | Bicep, parametriserad för dev/prod | ✅ Levererat |
| Automatiserad driftsättning | Två YAML-pipelines mot samma repo: GitHub Actions och Azure DevOps, båda med federerad identitet utan lagrade hemligheter | ✅ Levererat |
| API-dokumentation | Swagger UI (`/swagger`) | ✅ Levererat |
| Autoskalning | HTTP-baserad skalningsregel | ✅ Levererat |
| Rollback | Revisionshantering i Container Apps, dokumenterad och demonstrerad | ✅ Levererat |

### Utanför leveransens scope

| Punkt | Motivering |
|---|---|
| Autentisering per kund | Listnings-endpointen skyddas i dag av en gemensam API-nyckel. Inför kundlansering krävs Entra ID-integration eller separata nycklar per kund. |
| Rate limiting på verifieringsendpointen | Endpointen är medvetet öppen eftersom verifiering ska kunna göras av vem som helst. Vid kraftig trafiktopp rekommenderas cachning och throttling — se Kvarvarande risker. |
| Monitoring med larm | Loggar samlas i Log Analytics, men Application Insights med larm är inte uppsatt. Rekommenderas före produktionssättning. |
| Disaster recovery-plan | Utanför tidsscope för denna sprint. Bör definieras innan produktionssättning, inklusive geo-redundant lagring. |

## Arkitektur

### Systemdiagram

```
Drift

[Klient / Verifierare]
        │  HTTPS
        ▼
[Azure Container Apps — REST API, 2–3 repliker]
        │  Managed Identity
        ▼
[Azure Blob Storage — ett JSON-dokument per certifikat, namngivet med GUID]


Leverans

[git push till main på GitHub]
        │
        ├──► [GitHub Actions — bygg, testa, logga in via OIDC]
        │
        └──► [Azure DevOps Pipelines — bygg, testa, logga in via workload identity federation]
                        │
                        ▼
        [Azure Container Registry — image taggad med commit-hash]
                        │
                        ▼
        [Container App — ny revision, trafik växlas när den är frisk]
```

### Motiverade arkitekturval

**Varför Azure Container Apps och inte AKS?**
Container Apps ger containerbaserad drift med inbyggd autoskalning, revisionshantering och Managed Identity, utan att kunden behöver drifta ett Kubernetes-kluster. För en enskild, tillståndslös API-tjänst är ett dedikerat kluster ett driftsåtagande utan motsvarande nytta. AKS blir aktuellt först om plattformen växer till flera samverkande tjänster med behov av service mesh, egna operatorer eller specifik nodkontroll.

**Varför Bicep och inte manuell konfiguration?**
Hela infrastrukturen — resurser, roller och inställningar — beskrivs i en fil som versionshanteras tillsammans med koden. Mallen är idempotent: den kan köras om hur många gånger som helst och landar alltid i samma önskade tillstånd. Det gör det säkert att köra om en avbruten deploy, och en förhandsgranskning visar exakt vad som kommer ändras innan något rörs.

**Varför Azure Blob Storage för fillagring?**
Certifikatdata är små, oföränderliga JSON-dokument som slås upp via ett GUID — exakt det mönster Blob Storage är byggt för, till en bråkdel av kostnaden för en databas. Managed Identity ger applikationen åtkomst utan någon lagrad nyckel.

**Varför två pipelines?**
GitHub Actions och Azure DevOps kör identiska steg mot samma repo. Det visar att leveransflödet inte är bundet till ett verktyg — samma Dockerfile, samma Bicep, samma kommandon — och ger kunden frihet att välja plattform utan att bygga om något.

## Säkerhetsarkitektur

### Identitet och åtkomst

| Resurs | Åtkomstkontroll |
|---|---|
| Azure Container Apps | Systemtilldelad Managed Identity — ingen nyckel |
| Azure Blob Storage | RBAC via Managed Identity (Storage Blob Data Contributor) |
| Azure Container Registry | RBAC via Managed Identity (AcrPull) — adminanvändare avstängd |
| Pipeline-credentials, GitHub Actions | OIDC federated credential — ingen lagrad klienthemlighet, bara tokens utfärdade för exakt detta repo och denna branch accepteras |
| Pipeline-credentials, Azure DevOps | Service connection med workload identity federation — samma princip, ingen lagrad hemlighet |

### Hemlighetshantering

Inga credentials lagras i källkod eller git-historik. Applikationen och båda pipelinerna autentiserar sig med identiteter, inte nycklar. Den enda applikationsspecifika hemligheten — API-nyckeln för listnings-endpointen — skickas som en säker parameter vid deploy och lagras som ett Container Apps-secret som applikationen läser via miljövariabel. GitHub Secrets innehåller endast identifierare (client-, tenant- och prenumerations-ID), vilka saknar värde utan den federerade identiteten.

## Kvarvarande risker

| Risk | Sannolikhet | Åtgärd |
|---|---|---|
| Ingen rate limiting på den publika verifieringsendpointen | Hög vid viral spridning | Sätt `Cache-Control` på svaret (datat är oföränderligt), höj `maxReplicas`, vid stora volymer Azure Front Door med cachning |
| Gemensam API-nyckel istället för nyckel per kund | Medel | Implementera Entra ID Easy Auth eller nyckel per kund inför lansering |
| Inga larm vid fel eller hög svarstid | Medel | Koppla Application Insights, larm vid felfrekvens över 5 % eller svarstid över 2 sekunder |
| Ingen geo-redundans i lagringen (LRS) | Låg | Uppgradera till ZRS eller GRS om verksamhetskritisk drift kräver det |

## Kostnadskalkyl

Priser för Sweden Central från Azures officiella prislista, 2026-09-19. Full beräkning finns i ARCHITECTURE.md under Ekonomi.

### Månadskostnad vid lansering — 40 kunder, ca 8 000 certifikat/mån

| Resurs | SKU | Uppskattad kostnad/mån |
|---|---|---|
| Container Apps Environment | Consumption | 0 kr |
| Container App, 2 repliker (0,5 vCPU, 1 GiB) | Consumption, idle-taxa | 222 kr |
| Container App, aktiv beräkning och anrop | Consumption | 0 kr (inom frikvot) |
| Azure Container Registry | Basic | 49 kr |
| Azure Blob Storage | Standard LRS | under 1 kr |
| Log Analytics | Per GB | 0 kr (inom frikvot) |
| GitHub Actions / Azure DevOps | Fri kvot | 0 kr |
| **Totalt** | | **ca 270 kr/mån** |

Det motsvarar 0,034 kr per certifikat, eller knappt 7 % av en månadsintäkt på 3 960 kr. Vid en tredubbling av kundbasen (120 kunder, 24 000 certifikat) är kostnaden oförändrad, ca 270 kr, eftersom all faktisk användning fortfarande ryms inom Azures frikvoter — infrastrukturen sjunker då till 2,3 % av intäkten.

### Skalningspunkt

Kostnaden styrs inte av antalet certifikat utan av antalet ständigt aktiva repliker: varje ytterligare replika i vila kostar ca 111 kr/mån. Vid normal drift är systemet över 99 % i vila. Den verkliga gränsen är trafiktoppar: en replika hanterar uppskattningsvis 50–100 verifieringar per sekund, tre repliker 150–300. En certifikatlänk som delas 10 000 gånger på en dag kostar i praktiken ingenting (allt ryms i frikvoten) och ligger långt under kapacitetstaket. Först vid ihållande hundratals anrop per sekund behöver `maxReplicas` höjas eller cachning läggas framför verifieringsendpointen.

## Rekommendationer inför produktionssättning

1. **Autentisering per kund** — Entra ID Easy Auth eller API-nyckel per kund innan offentlig lansering.
2. **Monitoring** — Application Insights med larm vid felfrekvens över 5 % eller svarstid över 2 sekunder.
3. **Cachning av verifieringssvar** — `Cache-Control`-header på `/verify` direkt; Front Door först vid stora volymer.
4. **Kostnadslarm** — budgetlarm i Azure Cost Management vid 80 % av månadsbudget.
5. **Geo-redundans** — byt lagringskontot till ZRS eller GRS om tillgänglighetskraven kräver det.

## Överlämning

| Leverabel | Plats |
|---|---|
| Källkod | https://github.com/DevEssan/certify-api |
| Bicep-mallar | `infra/` i repot (`main.bicep`, `main.dev.bicepparam`, `main.prod.bicepparam`) |
| Pipeline, GitHub Actions | `.github/workflows/deploy.yml` |
| Pipeline, Azure DevOps | `azure-pipelines.yml` i repots rot |
| API-dokumentation | https://ca-certify-dev.mangopebble-0b9be043.swedencentral.azurecontainerapps.io/swagger |
| Teknisk reflektion och ekonomi | `ARCHITECTURE.md` i repots rot |
| Denna rapport | `RAPPORT.md` i repots rot |

---

*Denna rapport är framtagen som del av slutleveransen för Labb 3 — Full cloudlösning i team.*
*Datum: 2026-09-20 | Version: 1.0*