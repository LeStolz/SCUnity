# SCUnity (State Chart XML for Unity)

SCUnity is a visual editor and runtime interpreter for integrating [SCXML (State Chart XML)](https://www.w3.org/TR/scxml/) into your Unity projects. It lets you visually edit complex state machines in `.scxml` files.

## Runtime Classes

The package provides two primary runtime classes to manage your state machines in-game:

- **`SCStateMachine`**: A `MonoBehaviour` that acts as the controller for a state machine instance. You can assign an `.scxml` TextAsset or paste raw XML directly into it. When inspecting an `SCStateMachine`, click the "Open in Editor" button in the Inspector to launch the visual editor and start editing your logic!
- **`SCClient`**: A singleton client that handles the underlying communication and SCXML interpretation at runtime. It silently processes events and dispatches state changes back to your `SCStateMachine` instances.