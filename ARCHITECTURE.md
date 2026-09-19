# ARCHITECTURE.md — Certify

Teknisk reflektion för Certify AB (scenario D). Lösningen är byggd och driftsatt ensam.

## 1. Container Apps

Jag deployar till Azure Container Apps eftersom AKS är blockerat i prenumerationen — men jag hade valt Container Apps ändå. Certify är en enda tillståndslös API-tjänst, och Container Apps ger mig det jag faktiskt behöver från Kubernetes (containrar, repliker, autoskalning, revisioner, rullande uppdateringar) utan att jag behöver drifta ett kluster: inga noder att uppgradera, ingen kontrollplan att övervaka, och skalning till noll när ingen använder tjänsten. Managed Identity mot ACR och Blob Storage fungerar rakt av utan att jag konfigurerar något i klustret.

Begränsningarna är baksidan av samma mynt. Jag kan inte köra egna operatorer, DaemonSets eller CRD:er, jag har begränsad kontroll över nätverket och noderna, och jag kan inte använda Helm-charts eller kubectl mot miljön. Jag skulle välja AKS när tjänsten växer till flera samverkande tjänster som behöver service mesh, när jag behöver GPU-schemaläggning eller specifika nodtyper, eller när organisationen redan har Kubernetes-kompetens och verktyg (ArgoCD, Helm) som de vill återanvända. För ett startup med en tjänst och ingen driftavdelning är det overhead utan nytta.

## 2. CI/CD

Flödet från `git push` till live app: en push till `main` triggar GitHub Actions. Jobbet checkar ut koden, installerar .NET 10, kör `dotnet restore`, `dotnet build` i Release och `dotnet test` (xUnit). Därefter loggar det in i Azure via OIDC — GitHub utfärdar en kortlivad token som Azure accepterar bara för exakt det här repot och den här branchen, ingen lagrad hemlighet finns. Sedan byggs Docker-imagen med multi-stage Dockerfilen, taggas med commit-hashen och pushas till ACR. Sista steget kör `az containerapp update --image` med den nya taggen, vilket skapar en ny revision i Container Apps. Trafiken flyttas till den nya revisionen först när den rapporterar sig frisk.

Misslyckas bygget eller ett test stannar pipelinen på det steget. Ingen image byggs, inget deployas, och den tidigare revisionen fortsätter svara som om inget hänt. Jag fick se det i praktiken: min första körning stannade på Azure-inloggningen (fel format på federated credential-subjektet), och appen i Azure fortsatte köra den gamla versionen orörd tills jag rättat felet och kört om.

## 3. IaC

Jag definierar infrastrukturen i Bicep istället för i portalen för att den ska gå att läsa, granska och köra om. Varje resurs, varje roll och varje inställning står i en fil i git, så jag vet exakt vad som finns och varför, och det går att granska utan att någon behöver logga in i Azure. `az deployment group what-if` visar vad en ändring kommer göra innan den görs.

Idempotens betyder att samma mall kan köras hur många gånger som helst och alltid ger samma slutresultat — den skapar inte dubbletter och den felar inte för att något redan finns. Jag har kört mallen tre gånger: första gången låste den sig, andra gången skapade den allt, tredje gången ändrade den exakt en egenskap (`minReplicas: 0 => 2`) och lämnade resten orört. Det spelar roll för att jag kan köra om en avbruten deploy utan att städa, ändra en parameter utan att röra allt annat, och se på what-if-utskriften om verkligheten driftat från mallen.

## 4. Säkerhet

Det finns inga nycklar i lösningen. Appen når Blob Storage med en systemtilldelad Managed Identity som har rollen Storage Blob Data Contributor på exakt det lagringskontot, och hämtar sin image från ACR med samma identitet och rollen AcrPull. Pipelinen loggar in med OIDC federated credential — Azure litar på tokens som GitHub utfärdar för `DevEssan/certify-api` på `main`, inget annat. Den enda applikationshemligheten, API-nyckeln för `GET /certificates`, är en `@secure()`-parameter i Bicep som skickas in vid deploy och lagras som ett Container Apps-secret; koden läser den som miljövariabel via `secretRef`. GitHub Secrets innehåller bara identifierare (client-, tenant- och prenumerations-ID) som är värdelösa utan federated credential. `git grep` på nyckeln, prenumerations-ID:t och tenant-ID:t ger tomt.

Hamnar en nyckel i git-historiken måste jag utgå från att den är röjd i samma sekund som pushen gick, även om jag raderar committen efteråt — kloner, forkar och cacheminnen behåller den. Första åtgärden är att rotera eller återkalla nyckeln, inte att städa historiken. Därefter skriver jag om historiken (`git filter-repo`) och tvingar en push, och slår på GitHubs secret scanning och push protection så att det inte händer igen. Det verkliga skyddet är att jag med Managed Identity och OIDC inte har någon långlivad nyckel som kan läcka.

## Ekonomi

Priser för Sweden Central från Azures officiella prislista, hämtade 2026-09-19.

| Meter | Pris |
|---|---|
| Container Apps vCPU, aktiv | 0,000228 kr/sekund |
| Container Apps vCPU, idle | 0,0000286 kr/sekund |
| Container Apps minne, aktiv och idle | 0,0000286 kr/GiB-sekund |
| Container Apps anrop | 3,81 kr per miljon |
| Frikvot per månad | 180 000 vCPU-s, 360 000 GiB-s, 2 miljoner anrop |
| Container Registry Basic | 1,62 kr/dygn (10 GB ingår) |
| Blob Storage Hot LRS | ca 0,19 kr/GB/mån, ca 0,5 kr per 10 000 skrivningar |
| Log Analytics | 5 GB/mån gratis |
| Container Apps Environment, Consumption | ingen fast avgift |

Varje replika är 0,5 vCPU och 1 GiB. En månad är 2 592 000 sekunder.

### Månadskostnad vid lansering — 40 kunder, ca 8 000 certifikat/mån

| Post | Beräkning | Kostnad |
|---|---|---|
| Container App, 2 repliker i vila dygnet runt | vCPU: 2 × 0,5 × 2 592 000 × 0,0000286 = 74 kr. Minne: 2 × 1 × 2 592 000 × 0,0000286 = 148 kr | 222 kr |
| Container App, aktiv tid | ca 50 000 anrop × 0,1 s = 5 000 s — inom frikvoten | 0 kr |
| Anrop | ca 50 000 — inom frikvoten | 0 kr |
| Container Registry Basic | 30 × 1,62 | 49 kr |
| Blob Storage | 8 000 filer à 300 byte, 8 000 skrivningar | under 1 kr |
| Log Analytics, Environment | inom frikvot / ingen avgift | 0 kr |
| **Totalt** | | **ca 270 kr/mån** |

Det ger 0,034 kr per certifikat. Intäkten är 40 × 99 = 3 960 kr/mån, så infrastrukturen är knappt 7 % av omsättningen.

### Om kundbasen tredubblas — 120 kunder, ca 24 000 certifikat/mån

Ungefär 150 000 anrop och 15 000 aktiva sekunder per månad — fortfarande långt inom frikvoten. Lagringen växer till 1–2 kr. Totalen blir oförändrad, **ca 270 kr/mån**, vilket ger 0,011 kr per certifikat och 2,3 % av en omsättning på 11 880 kr. Kostnaden är i praktiken fast tills trafiken passerar frikvoterna, vilket vid den här belastningen kräver flera hundra kunder.

### Dyraste resursen och varför

Container Appen står för 222 av 270 kr, drygt 80 %. Orsaken är kravet på minst två repliker: jag betalar för 2 GiB minne och 1 vCPU i idle-taxa dygnet runt oavsett om någon använder tjänsten. Minne har ingen rabatterad idle-taxa, så det är minnet (148 kr) som väger tyngst, inte processorn. Det faktiska arbetet — anrop och aktiv beräkning — ryms i frikvoten och kostar noll. Under utveckling körde jag med `minReplicas: 0` (dev-parameterfilen), då faller posten till nära noll och månadskostnaden blir ca 50 kr, nästan helt ACR:s fasta avgift.

### Flaskhals om trafiken fyrdubblas

Fyrdubblad trafik från lanseringsnivån är ca 32 000 certifikat och 200 000 anrop per månad — 0,08 anrop per sekund i snitt. Ingen resurs är i närheten av en gräns; systemet är över 99 % i vila och kostnaden är oförändrad. Flaskhalsen i Certify är inte volym utan toppar, vilket leder till grundarens fråga.

### Grundarens fråga: 10 000 delningar av en certifikatlänk på en dag

Kostnaden är i praktiken noll: 10 000 anrop ryms i frikvoten på 2 miljoner, 10 000 blob-läsningar kostar ca 0,04 kr, och 10 000 × 0,05 s aktiv tid är 250 vCPU-sekunder, också inom frikvoten. Även en miljon verifieringar på en dag hamnar under 5 kr.

Kapaciteten är den intressanta gränsen. 10 000 anrop per dygn är 0,12 per sekund; kommer alla inom en timme är det 2,8 per sekund — en enda replika klarar det med god marginal. Varje verifiering är en blob-läsning på 20–50 ms, så en replika hanterar uppskattningsvis 50–100 anrop per sekund och tre repliker 150–300. Över det stiger svarstiderna och anrop börjar få 503.

Tre åtgärder, i kostnadsordning. För det första: höj `maxReplicas` — en extra replika kostar bara när den är aktiv, ca 0,5 kr per timme, och skalningsregeln startar den automatiskt. För det andra: sätt `Cache-Control` på `/verify` — certifikatdata ändras aldrig efter utfärdande, så webbläsare och mellanliggande proxyer kan svara utan att appen ens ser anropet, till noll kronor. För det tredje, först vid verkligt stora volymer: Azure Front Door med cachning framför appen, ca 350 kr/mån i grundavgift, motiverat först när trafiken är hundra gånger dagens.

## Lärdomar under bygget

Fyra saker gick fel, alla i infrastrukturen och alla lärorika. Container Appen låste sig i 21 minuter vid första deployen eftersom mallen kopplade ACR med systemidentitet innan AcrPull-rollen fanns — identiteten skapas av appen, rollen kräver identiteten, appen väntade på rollen. Lösningen är flaggan `attachRegistry` i mallen: false vid första deployen på en tom miljö, true därefter. Windows PowerShell skickade svenska tecken som Latin-1 och API:et svarade 400 tills jag skickade UTF-8-bytes. Visual Studios låsta `.vs`-mapp blockerade `az acr build` tills jag lade den i `.dockerignore`. Och GitHub hade ändrat formatet på OIDC-subjektet till att inkludera konto- och repo-ID, vilket felmeddelandet från Azure ordagrant talade om — den rättningen var ett ord. Gemensamt för alla fyra: felmeddelandet innehöll svaret, och what-if eller loggarna visade var jag skulle titta.