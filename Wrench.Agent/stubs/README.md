Compile-only stand-ins for the Project Zomboid classes the agent links against. They are never packaged:
at runtime the real classes come from `projectzomboid.jar`. Only declare members the agent uses, with
signatures matching `javap -cp projectzomboid.jar <class>` exactly.
