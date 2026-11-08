"""Exercise server-rendered responses. This does not perform browser acceptance."""
import html
import json
import os
import pathlib
import re
import socket
import subprocess
import time
import uuid
import urllib.error
import urllib.request
import xml.etree.ElementTree as ET

root = pathlib.Path(__file__).resolve().parents[1]
sdk = root / '.tools/dotnet/dotnet.exe'
environment = os.environ.copy()
environment.update({
    'DOTNET_ROOT': str(sdk.parent),
    'DOTNET_CLI_HOME': str(root / '.tools/cli-home'),
    'NUGET_PACKAGES': str(root / '.tools/packages'),
    'ASPNETCORE_ENVIRONMENT': 'SmokeTest',
    'DOTNET_ENVIRONMENT': 'SmokeTest',
})
run_directory = root / '.tools' / 'smoke-runs' / str(uuid.uuid4())
run_directory.mkdir(parents=True)
environment['ForgeFlow__Smoke__KeyDirectory'] = str(run_directory / 'keys')
with socket.socket() as available:
    available.bind(('127.0.0.1', 0))
    port = available.getsockname()[1]
base = f'http://127.0.0.1:{port}'
results = []


def request(path, headers=None):
    req = urllib.request.Request(base + path, headers=headers or {})
    try:
        response = urllib.request.urlopen(req, timeout=15)
    except urllib.error.HTTPError as error:
        response = error
    with response:
        return response.status, response.headers, response.read().decode('utf-8-sig')


def catalog(resource, culture):
    path = root / f'src/ForgeFlow.Web/Resources/Localization/{resource}.{culture}.resx'
    return {entry.attrib['name']: entry.findtext('value') for entry in ET.parse(path).getroot().findall('data')}


with (root / 'verification/server.log').open('w', encoding='utf-8') as log:
    process = subprocess.Popen([
        str(sdk), str(root / 'src/ForgeFlow.Web/bin/Release/net10.0/ForgeFlow.Web.dll'), '--urls', base,
    ], cwd=root / 'src/ForgeFlow.Web', env=environment, stdout=log, stderr=subprocess.STDOUT)
    try:
        for attempt in range(100):
            if process.poll() is not None:
                raise RuntimeError('Server exited before readiness; see verification/server.log')
            try:
                status, _, _ = request('/')
                if status == 200:
                    break
            except (OSError, urllib.error.URLError):
                time.sleep(0.2)
        else:
            raise RuntimeError('Server readiness timed out')

        for culture, direction, date in [('fa-IR', 'rtl', '1405/01/01'), ('en-US', 'ltr', '2026/03/21')]:
            status, headers, body = request('/', {'Accept-Language': culture})
            assert status == 200, status
            decoded = html.unescape(body)
            assert f'lang="{culture}"' in decoded
            assert f'dir="{direction}"' in decoded
            assert headers.get('Content-Language') == culture
            messages = catalog('SampleResources', culture)
            for key in ['Title', 'NameLabel', 'ValidateButton', 'DateLabel', 'NumberLabel']:
                assert messages[key] in decoded, key
            assert date in decoded
            assert '<button' in body and 'type="submit"' in body
            assert '<!--Blazor:' in body, 'Interactive component marker missing'
            assert 'name="__RequestVerificationToken"' in body, 'Antiforgery form token missing'
            assert any('.AspNetCore.Antiforgery.' in cookie for cookie in headers.get_all('Set-Cookie', [])), 'Antiforgery cookie missing'
            saved_body = re.sub(r'(<input\b[^>]*name="__RequestVerificationToken"[^>]*value=")[^"]*', r'\1[redacted]', body)
            (root / f'verification/sample.{culture}.html').write_text(saved_body, encoding='utf-8')
            assets = re.findall(r'(?:href|src)="([^\"]+\.(?:css|js)(?:\?[^\"]*)?)"', body)
            assets.append('js/drafts.mjs')
            assert any('Bit.BlazorUI/styles/' in asset for asset in assets)
            assert any('Bit.BlazorUI/scripts/' in asset for asset in assets)
            assert any('_framework/blazor.web.' in asset for asset in assets)
            assert any(re.search(r'(?:^|/)app(?:\.[^.]+)?\.css', asset) for asset in assets)
            asset_results = []
            for asset in assets:
                path = '/' + html.unescape(asset).lstrip('/')
                asset_status, asset_headers, content = request(path)
                assert asset_status == 200, (path, asset_status)
                content_type = asset_headers.get_content_type()
                assert content_type in ('text/css', 'text/javascript', 'application/javascript'), (path, content_type)
                assert content, path
                asset_results.append({'path': path, 'status': asset_status, 'contentType': content_type})
            shared = catalog('SharedResources', culture)
            not_found_status, _, not_found = request('/unknown-route', {'Accept-Language': culture})
            assert not_found_status == 404, not_found_status
            assert shared['NotFoundTitle'] in html.unescape(not_found)
            error_status, _, error = request('/error', {'Accept-Language': culture})
            assert error_status == 200
            assert shared['UnexpectedError'] in html.unescape(error)
            results.append({'culture': culture, 'direction': direction, 'http': status,
                            'date': date, 'assetCount': len(assets), 'assets': asset_results,
                            'antiforgeryTokenAndCookie': 'present; values not recorded',
                            'localizedNotFound': not_found_status, 'localizedErrorPage': 'passed'})

        _, _, default_body = request('/')
        assert 'lang="fa-IR"' in default_body
        _, _, query_body = request('/?culture=en-US&ui-culture=en-US')
        assert 'lang="fa-IR"' in query_body
        _, _, cookie_body = request('/', {'Accept-Language': 'fa-IR', 'Cookie': '.AspNetCore.Culture=c=en-US|uic=en-US'})
        assert 'lang="en-US"' in cookie_body
        _, _, unsupported = request('/', {'Accept-Language': 'xx-XX'})
        assert 'lang="fa-IR"' in unsupported
        key_files = list((run_directory / 'keys').glob('key-*.xml'))
        assert key_files, 'No persisted Data Protection key found'
        for key_file in key_files:
            tags = [element.tag.rsplit('}', 1)[-1] for element in ET.parse(key_file).getroot().iter()]
            assert 'encryptedSecret' in tags and 'EncryptedData' in tags
            assert 'masterKey' not in tags, 'Unencrypted master key found'
        (root / 'verification/http-smoke.json').write_text(json.dumps({
            'status': 'passed', 'environment': 'SmokeTest',
            'dataProtection': {'keyDirectory': str(run_directory / 'keys'), 'encryptedKeyFiles': len(key_files),
                               'plaintextMasterKeys': False, 'certificatePrivateKey': 'in memory for this host lifetime'},
            'responses': results, 'defaultCulture': 'passed', 'queryOverrideDisabled': 'passed',
            'cookiePrecedence': 'passed', 'unsupportedHeaderFallback': 'passed',
            'browserAcceptance': 'not performed',
        }, indent=2) + '\n', encoding='utf-8')
        print('HTTP smoke passed for both cultures, localized routes, assets, and request-culture precedence.')
    except Exception as error:
        (root / 'verification/http-smoke.json').write_text(json.dumps({
            'status': 'failed', 'command': 'python scripts/smoke-http.py',
            'error': str(error), 'completedCultures': results,
            'serverLog': 'verification/server.log', 'browserAcceptance': 'not performed',
        }, indent=2) + '\n', encoding='utf-8')
        raise
    finally:
        process.terminate()
        try:
            process.wait(timeout=10)
        except subprocess.TimeoutExpired:
            process.kill()
            process.wait(timeout=10)
