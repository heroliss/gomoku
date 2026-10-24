# Photon networking notice

The networking module uses the unmodified Photon .NET client SDK assemblies
already present in the original 2017 Gomoku project:

| Assembly | Assembly version | SHA-256 |
| --- | --- | --- |
| Photon3DotNet.dll | 4.0.5.0 | `5ce4047127715d4e8e66c409de63d284d8a9b8dce9f4368e4c15019e55ec2114` |
| PhotonLoadbalancingApi.dll | 4.0.5.0 | `cfd0cdc167ec5ebd619c4d5de2e5e98db4853b5ecdf096fd758f940537945aea` |

Photon and its SDK are proprietary software by Exit Games GmbH. They are not
covered by any license of the original Liss game code. Keep the original
copyright information intact. The game distribution uses these libraries only
as integrated dependencies of the multiplayer game; do not distribute a
standalone SDK mirror or treat the SDK as open source.

Official terms and downloads:

- [Photon license terms](https://www.photonengine.com/terms/licenseterms)
- [Previous terms for existing customers](https://www.photonengine.com/en-US/terms/archive/photon-license-terms)
- [Official SDK downloads](https://www.photonengine.com/sdks)
- [.NET SDK third-party notices](https://doc.photonengine.com/docs/content/OSS-.NET_Client_SDKs.pdf)

`Photon-NET-OSS-current.pdf` is a copy of the current official .NET SDK third-party
notices retrieved on 2026-10-24. The legacy backup did not include an original
license file. This current notice document does not assert that the old 4.0.5
SDK uses every dependency in the current SDK, and does not replace the applicable
Photon agreement. Include this notice and the official notice PDF with the
complete game release.

The checked-in application identifier is a public Photon client AppId recovered
from the original game, not an account password, secret key or dashboard token.
Do not add Photon account credentials to this project. Photon Cloud availability,
region access and CCU limits are controlled by the original application account.

## Building without the local legacy backup

The local modernization workspace references the original SDK from
`legacy/v2.3/五子棋/Lib` as a fallback. A source-only checkout can instead use
`vendor/Photon` (ignored from source control), containing the same two assemblies
from a legally obtained original SDK or this application's complete Windows
release. An explicit SDK directory can be set with
`dotnet build -p:PhotonSdkDirectory="C:\path\to\Photon"`.

The latest SDK download changes APIs/namespaces and cannot simply replace these
files. A later migration must be implemented and tested against the official SDK
as a separate change. The current game does not load code from a network location.
