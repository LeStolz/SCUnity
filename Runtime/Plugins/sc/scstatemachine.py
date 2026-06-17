from PySide6.QtCore import QCoreApplication, QByteArray, QBuffer, QIODevice
from PySide6.QtScxml import QScxmlStateMachine
import json, sys


machines: dict = {}
_qt_app = None


def ensure_qt_app():
    global _qt_app

    if _qt_app is None:
        _qt_app = QCoreApplication.instance()
        if _qt_app is None:
            _qt_app = QCoreApplication([])

    return _qt_app


def pump_qt_events():
    app = ensure_qt_app()
    app.processEvents()


def log(msg):
    emit({
        "ok": True,
        "type": "log",
        "data": f"[SCPython] {msg}",
    })


def emit(msg):
    pump_qt_events()
    sys.stdout.write(json.dumps(msg) + "\n")
    sys.stdout.flush()


def create_statemachine(id, name: str, data: str):
    def on_finished():
        emit({
            "ok": True,
            "type": "finished",
            "name": name
        })


    def on_log(label, msg):
        if label == "" and msg == "":
            return

        if label.startswith("send:"):
            emit({
                "ok": True,
                "type": "eventSent",
                "name": name,
                "data": json.dumps({
                    "target": label[5:],
                    "data": msg
                })
            })
            return

        log(f"{label}: {msg}")


    def on_states_changed():
        emit({
            "ok": True,
            "type": "statesChanged",
            "name": name,
            "data": json.dumps({
                "activeStates": m.activeStateNames()
            }),
        })


    ensure_qt_app()

    bytes = QByteArray(data.decode())
    buffer = QBuffer(bytes)
    buffer.open(QIODevice.OpenModeFlag.ReadWrite)
    m = QScxmlStateMachine.fromData(buffer)

    parse_errors = m.parseErrors()
    if parse_errors:
        emit({
            "ok": False,
            "id": id,
            "name": name,
            "data": f"Failed to parse SCXML {', '.join(str(err) for err in parse_errors)}",
        })
        return

    if m is None or not m.init():
        emit({
            "ok": False,
            "id": id,
            "name": name,
            "data": "Failed to initialize SCXML state machine",
        })
        return


    m.start()
    m.finished.connect(on_finished)
    m.log.connect(on_log)
    m.reachedStableState.connect(on_states_changed)
    # m.connectToEvent("*", event_handler, "on_event")

    machines[name] = {
        "machine": m,
        # "event_handler": event_handler
    }

    pump_qt_events()

    emit({
        "ok": True,
        "id": id,
        "name": name,
    })


def send_event(id, name, data):
    data = json.loads(data.decode()) if data else None
    machine = machines.get(name)

    if data.get("event") is None or machine is None:
        get_active_states(id, name)
        return

    machine["machine"].submitEvent(data["event"], data.get("data"))
    get_active_states(id, name)


def get_active_states(id, name):
    pump_qt_events()

    machine = machines.get(name)

    emit({
        "ok": machine is not None,
        "id": id,
        "data": json.dumps({
            "activeStates": machine["machine"].activeStateNames() if machine is not None else []
        }),
    })


def get_value(id, name, data):
    data = json.loads(data.decode()) if data else None
    machine = machines.get(name)

    if data.get("key") is None or machine is None:
        emit({
            "ok": False,
            "id": id,
            "data": None,
        })
        return

    value = machine["machine"].dataModel().property(data["key"])
    emit({
        "ok": True,
        "id": id,
        "data": json.dumps(value) if value is not None else None,
    })


def destroy_statemachine(id, name):
    if name in machines:
        machines[name]["machine"].stop()
        machines[name]["machine"].deleteLater()
        del machines[name]

    emit({
        "ok": True,
        "id": id,
        "name": name,
    })