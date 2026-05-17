import { useCallback, useState } from "react";
import { open } from "@tauri-apps/plugin-dialog";
import { useReposStore } from "../store";
import { useAlertStore } from "../../../shared/stores/alerts";

export function useOpenRepo() {
  const openRepo = useReposStore((s) => s.openRepo);
  const addAlert = useAlertStore((s) => s.addAlert);
  const [loading, setLoading] = useState(false);

  const browse = useCallback(async () => {
    const selected = await open({ directory: true, multiple: false });
    if (!selected) return;

    setLoading(true);
    const err = await openRepo(selected);
    setLoading(false);

    if (err) {
      addAlert(err);
    }
  }, [openRepo, addAlert]);

  return { browse, loading };
}
