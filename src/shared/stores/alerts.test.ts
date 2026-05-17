import { describe, it, expect, beforeEach } from "vitest";
import { useAlertStore } from "./alerts";

beforeEach(() => {
  useAlertStore.setState({ alerts: [] });
});

describe("useAlertStore", () => {
  it("starts with no alerts", () => {
    expect(useAlertStore.getState().alerts).toEqual([]);
  });

  it("addAlert adds an error alert by default", () => {
    useAlertStore.getState().addAlert("something went wrong");
    const alerts = useAlertStore.getState().alerts;
    expect(alerts).toHaveLength(1);
    expect(alerts[0].type).toBe("error");
    expect(alerts[0]).toHaveProperty("message", "something went wrong");
  });

  it("addAlert supports warning and info types", () => {
    useAlertStore.getState().addAlert("heads up", "warning");
    useAlertStore.getState().addAlert("fyi", "info");
    const alerts = useAlertStore.getState().alerts;
    expect(alerts[0].type).toBe("warning");
    expect(alerts[1].type).toBe("info");
  });

  it("removeAlert removes by id", () => {
    useAlertStore.getState().addAlert("a");
    useAlertStore.getState().addAlert("b");
    const [first] = useAlertStore.getState().alerts;
    useAlertStore.getState().removeAlert(first.id);
    const remaining = useAlertStore.getState().alerts;
    expect(remaining).toHaveLength(1);
    expect(remaining[0]).toHaveProperty("message", "b");
  });

  it("addUpdateAlert adds an update alert", () => {
    useAlertStore.getState().addUpdateAlert("2.0.0");
    const alerts = useAlertStore.getState().alerts;
    expect(alerts).toHaveLength(1);
    expect(alerts[0].type).toBe("update");
    expect(alerts[0]).toHaveProperty("version", "2.0.0");
  });

  it("addUpdateAlert replaces existing update alert", () => {
    useAlertStore.getState().addUpdateAlert("2.0.0");
    useAlertStore.getState().addUpdateAlert("3.0.0");
    const updates = useAlertStore.getState().alerts.filter((a) => a.type === "update");
    expect(updates).toHaveLength(1);
    expect(updates[0]).toHaveProperty("version", "3.0.0");
  });

  it("regular alerts coexist with update alert", () => {
    useAlertStore.getState().addAlert("error happened");
    useAlertStore.getState().addUpdateAlert("2.0.0");
    expect(useAlertStore.getState().alerts).toHaveLength(2);
  });
});
