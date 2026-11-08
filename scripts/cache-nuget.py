"""Populate a local feed from official NuGet HTTPS endpoints, without relaxing TLS.

Downloads compatible dependency candidates; NuGet remains the actual resolver and
signature verifier. This is an environment transport fallback, not a replacement
for normal online restore or vulnerability auditing.
"""
import hashlib
import json
import pathlib
import re
import urllib.request
import xml.etree.ElementTree as ET
import zipfile

root = pathlib.Path(__file__).resolve().parents[1]
feed = root / '.tools' / 'nuget-feed'
feed.mkdir(parents=True, exist_ok=True)
props = ET.parse(root / 'Directory.Packages.props')
queue = [(p.attrib['Include'].lower(), p.attrib['Version']) for p in props.iter('PackageVersion')]
web_project = ET.parse(root / 'src/ForgeFlow.Web/ForgeFlow.Web.csproj')
queue.append(('microsoft.aspnetcore.app.internal.assets', web_project.findtext('.//RuntimeFrameworkVersion')))
seen = set()
evidence = []


def framework_rank(name):
    name = name.lower().replace('.netstandard', 'netstandard').replace('.netcoreapp', 'netcoreapp')
    match = re.fullmatch(r'(netstandard|netcoreapp|net)(\d+)\.(\d+)', name)
    if not match:
        return -1
    kind, major, minor = match.groups()
    version = int(major) * 100 + int(minor)
    if kind == 'netstandard' and version <= 201:
        return 1000 + version
    if kind in ('netcoreapp', 'net') and version <= 1000:
        return 2000 + version
    return -1


while queue:
    name, version = queue.pop(0)
    if (name, version) in seen:
        continue
    seen.add((name, version))
    url = f'https://api.nuget.org/v3-flatcontainer/{name}/{version}/{name}.{version}.nupkg'
    path = feed / f'{name}.{version}.nupkg'
    if not path.exists():
        print(f'Downloading {name} {version}', flush=True)
        with urllib.request.urlopen(url, timeout=60) as response:
            path.write_bytes(response.read())
    with zipfile.ZipFile(path) as archive:
        nuspec = ET.fromstring(archive.read(next(n for n in archive.namelist() if n.endswith('.nuspec'))))
        # Namespace removal is limited to the in-memory inspection tree.
        for element in nuspec.iter():
            element.tag = element.tag.rsplit('}', 1)[-1]
        dependencies = nuspec.find('metadata/dependencies')
        if dependencies is not None:
            groups = list(dependencies.findall('group'))
            compatible = [g for g in groups if framework_rank(g.attrib.get('targetFramework', '')) >= 0]
            selected = max(compatible, key=lambda g: framework_rank(g.attrib['targetFramework'])) if compatible else dependencies
            for dependency in selected.findall('dependency'):
                minimum = dependency.attrib['version'].strip('[]()').split(',')[0].strip()
                if not minimum:
                    raise ValueError('Dependency without minimum: ' + ET.tostring(dependency).decode())
                queue.append((dependency.attrib['id'].lower(), minimum))
    evidence.append({'id': name, 'version': version, 'source': url, 'sha512': hashlib.file_digest(path.open('rb'), 'sha512').hexdigest()})

(root / 'verification' / 'package-downloads.json').write_text(json.dumps(evidence, indent=2) + '\n', encoding='utf-8')
print(f'Cached {len(evidence)} dependency candidates; NuGet must resolve and verify them.', flush=True)
