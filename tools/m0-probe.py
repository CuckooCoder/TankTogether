"""Run the isolated editor experiment through project-guarded local MCP."""
import asyncio
import json
import sys
import time
from pathlib import Path
from fastmcp import Client

ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / 'Unity/TankTogether'

async def main():
    async with Client('http://127.0.0.1:8088/mcp', timeout=60) as client:
        async def read(uri):
            return json.loads((await client.read_resource(uri))[0].text)
        instances = await read('mcpforunity://instances')
        candidates = [i for i in instances['instances'] if i['name'] == 'TankTogether']
        assert len(candidates) == 1, 'Expected exactly one TankTogether editor'
        await client.call_tool('set_active_instance', {'instance': candidates[0]['id']})
        info = await read('mcpforunity://project/info')
        assert Path(info['data']['projectRoot']).resolve() == PROJECT.resolve(), info
        print(json.dumps(info, ensure_ascii=True))
        action = sys.argv[1] if len(sys.argv) > 1 else 'inspect'
        if action in ('steam-shot', 'steam-host', 'steam-leave'):
            active = json.loads((await client.call_tool('manage_scene', {'action': 'get_active'})).content[0].text)
            assert active['data']['path'] == 'Assets/Scenes/SteamCarryLab.unity', active
            code = {'steam-shot': 'UnityEditor.EditorApplication.ExecuteMenuItem("Window/General/Game"); UnityEngine.ScreenCapture.CaptureScreenshot("Logs/steam-editor.png"); return "Screenshot requested";', 'steam-host': 'var s = UnityEngine.Object.FindAnyObjectByType<TankTogether.Networking.SteamCarrySession>(); s.CreateRoom(); return "Host requested";', 'steam-leave': 'var s = UnityEngine.Object.FindAnyObjectByType<TankTogether.Networking.SteamCarrySession>(); s.Leave(); return "Leave requested";'}[action]
            print((await client.call_tool('execute_code', {'action': 'execute', 'code': code})).content[0].text)
        elif action == 'reimport-steam':
            print((await client.call_tool('execute_code', {'action': 'execute', 'code': 'UnityEditor.AssetDatabase.ImportAsset("Packages/com.community.netcode.transport.facepunch/Runtime/Facepunch/Facepunch.Steamworks.Win64.dll", UnityEditor.ImportAssetOptions.ForceUpdate); UnityEditor.AssetDatabase.ImportAsset("Packages/com.community.netcode.transport.facepunch/Runtime/Facepunch/redistributable_bin/win64/steam_api64.dll", UnityEditor.ImportAssetOptions.ForceUpdate); return "Steam libraries reimported";'})).content[0].text)
        elif action == 'resolve':
            print((await client.call_tool('manage_packages', {'action': 'resolve_packages'})).content[0].text)
        elif action == 'refresh':
            result = await client.call_tool('refresh_unity', {'mode': 'force', 'scope': 'all', 'compile': 'request', 'wait_for_ready': True})
            print(result.content[0].text)
        elif action == 'run':
            state = await read('mcpforunity://editor/state')
            assert state['data']['advice']['ready_for_tools'], state
            assert state['data']['editor']['play_mode']['is_playing'], 'Enter Play mode first'
            started = time.time()
            result = await client.call_tool('execute_menu_item', {'menu_path': 'Tank Together/M0/Run Planar Physics Probe'})
            print(result.content[0].text)
            assert json.loads(result.content[0].text).get('success'), result
            report_path = PROJECT / 'Logs/m0-physics-probe.json'
            assert report_path.exists() and report_path.stat().st_mtime >= started - 1, 'No fresh experiment report'
            report = json.loads(report_path.read_text(encoding='utf-8'))
            assert report['passed'] and all(c['passed'] for c in report['checks']), report
            print(json.dumps({'fresh_report': True, 'checks_passed': len(report['checks']), 'scope': report['scope']}))
        elif action in ('play', 'stop'):
            print((await client.call_tool('manage_editor', {'action': action})).content[0].text)
        elif action in ('open-lab', 'check-lab', 'build-lab', 'open-steam', 'check-steam', 'build-steam', 'measure', 'apply-baseline', 'open-m1', 'check-m1', 'build-m1'):
            menus = {'open-lab': 'Tank Together/M0/Open Carry Lab', 'check-lab': 'Tank Together/M0/Run Carry Checks', 'build-lab': 'Tank Together/M0/Build Carry Lab Windows', 'open-steam': 'Tank Together/Steam/Open Network Lab', 'check-steam': 'Tank Together/Steam/Check Protocol', 'build-steam': 'Tank Together/Steam/Build Windows', 'measure': 'Tank Together/M0/Measure Motion Baseline', 'apply-baseline': 'Tank Together/M0/Apply Document Baseline'}
            menus.update({'open-m1': 'Tank Together/M1/Open Interaction Lab', 'check-m1': 'Tank Together/M1/Run Interaction Checks', 'build-m1': 'Tank Together/M1/Build Windows'})
            await client.read_resource('mcpforunity://menu-items')
            result = await client.call_tool('execute_menu_item', {'menu_path': menus[action]})
            print(result.content[0].text)
            assert json.loads(result.content[0].text).get('success'), result
            if action == 'check-steam':
                report_path = PROJECT / 'Logs/steam-protocol-checks.json'
                assert report_path.exists() and time.time() - report_path.stat().st_mtime < 15, 'No fresh protocol report'
                report = json.loads(report_path.read_text(encoding='utf-8'))
                assert report['passed'], report
                print(json.dumps({'passed': True, 'checks': len(report['checks'])}))
        elif action != 'inspect':
            raise ValueError(action)
        for name, args in [('manage_scene', {'action': 'get_active'}), ('read_console', {'action': 'get', 'types': ['error'], 'count': 20})]:
            result = json.loads((await client.call_tool(name, args)).content[0].text)
            print(json.dumps(result, ensure_ascii=True))
            assert result.get('success'), result
            if action == 'run' and name == 'read_console':
                assert not result['data'], result

if __name__ == '__main__':
    asyncio.run(main())
