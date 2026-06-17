import sys, json, os, base64
sys.path.insert(0, os.path.dirname(os.path.abspath(__file__)))
import scstatemachine as sc


for line in sys.stdin:
    cmd: dict = json.loads(line)
    op = cmd["op"]

    cmd["data"] = base64.b64decode(cmd["data"]) if "data" in cmd and cmd["data"] else None

    if op == "createStateMachine":
        sc.create_statemachine(cmd["id"], cmd["name"], cmd["data"])

    elif op == "sendEvent":
        sc.send_event(cmd["id"], cmd["name"], cmd["data"])

    elif op == "getActiveStates":
        sc.get_active_states(cmd["id"], cmd["name"])

    elif op == "getValue":
        sc.get_value(cmd["id"], cmd["name"], cmd["data"])

    elif op == "destroyStateMachine":
        sc.destroy_statemachine(cmd["id"], cmd["name"])