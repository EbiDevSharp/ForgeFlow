import hashlib, json, pathlib, urllib.request, zipfile
root = pathlib.Path(__file__).resolve().parents[1]
target = root / '.tools'
version = '10.0.403'
url = 'https://builds.dotnet.microsoft.com/dotnet/release-metadata/10.0/releases.json'
with urllib.request.urlopen(url, timeout=60) as response:
    raw = response.read()
metadata = json.loads(raw)
(target / 'dotnet-release-metadata.json').write_bytes(raw)
release = next(r for r in metadata['releases'] if r.get('sdk', {}).get('version') == version)
file = next(f for f in release['sdk']['files'] if f['name'] == 'dotnet-sdk-win-x64.zip')
archive = target / 'dotnet-sdk.zip'
print('Downloading SDK ' + version, flush=True)
with urllib.request.urlopen(file['url'], timeout=60) as response, archive.open('wb') as output:
    while chunk := response.read(1024 * 1024):
        output.write(chunk)
actual = hashlib.file_digest(archive.open('rb'), 'sha512').hexdigest()
if actual.lower() != file['hash'].lower():
    raise RuntimeError('SDK SHA512 mismatch')
print('SDK SHA512 verified: ' + actual, flush=True)
with zipfile.ZipFile(archive) as package:
    package.extractall(target / 'dotnet')
print('SDK extracted into workspace .tools/dotnet', flush=True)
