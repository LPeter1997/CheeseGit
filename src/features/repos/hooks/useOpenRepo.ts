import { useCallback, useState } from "react";
import { open } from "@tauri-apps/plugin-dialog";
import { useReposStore } from "../store";
import { useToastStore } from "../../../shared/stores/toast";

export function useOpenRepo() {
  const openRepo = useReposStore((s) => s.openRepo);
  const addToast = useToastStore((s) => s.addToast);
  const [loading, setLoading] = useState(false);

  const browse = useCallback(async () => {
    const selected = await open({ directory: true, multiple: false });
    if (!selected) return;

    setLoading(true);
    const err = await openRepo(selected);
    setLoading(false);

    if (err) {
      addToast(err);
    }
  }, [openRepo, addToast]);

  return { browse, loading };
}
