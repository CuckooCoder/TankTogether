"""Read-only diagnostics: select TankTogether and verify the full project path."""
import asyncio
import json
from pathlib import Path
from fastmcp import Client
ROOT = Path(__file__).resolve().parents[1]
PROJECT = ROOT / "Unity/TankTogether"
async def main():
    report = {}
    async with Client("http://127.0.0.1:8088/mcp", timeout=45) as c:
        async def resource(uri):
            return json.loads((await c.read_resource(uri))[0].text)
        report["instances"] = await resource("mcpforunity://instances")
        candidates = [i for i in report["instances"]["instances"] if i["name"] == "TankTogether"]
        assert len(candidates) == 1, "Expected exactly one TankTogether instance"
        await c.call_tool("set_active_instance", {"instance": candidates[0]["id"]})
        report["project"] = await resource("mcpforunity://project/info")
        assert Path(report["project"]["data"]["projectRoot"]).resolve() == PROJECT
        report["editor"] = await resource("mcpforunity://editor/state")
        for name,args in [("manage_scene", {"action":"get_active"}), ("read_console", {"action":"get", "types":["error"], "count":20})]:
            result = await c.call_tool(name,args)
            report[name] = json.loads(result.content[0].text)
            assert report[name].get("success"), report[name]
        assert not report["read_console"]["data"], report["read_console"]
        assert report["manage_scene"]["data"]["path"] == "Assets/Scenes/Main.unity"
        report["passed"] = True
    (PROJECT/"Logs/mcp-check.json").write_text(json.dumps(report,ensure_ascii=False,indent=2),encoding="utf-8")
    print(json.dumps(report,ensure_ascii=True))
if __name__ == "__main__":
    asyncio.run(main())
