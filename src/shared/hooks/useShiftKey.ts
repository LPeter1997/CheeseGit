import { useEffect, useState } from "react";

/** Track whether the Shift key is currently held. */
export function useShiftKey(): boolean {
  const [shift, setShift] = useState(false);
  useEffect(() => {
    const down = (e: KeyboardEvent) => { if (e.key === "Shift") setShift(true); };
    const up = (e: KeyboardEvent) => { if (e.key === "Shift") setShift(false); };
    window.addEventListener("keydown", down);
    window.addEventListener("keyup", up);
    // Reset when window loses focus (shift might be released while blurred)
    const blur = () => setShift(false);
    window.addEventListener("blur", blur);
    return () => {
      window.removeEventListener("keydown", down);
      window.removeEventListener("keyup", up);
      window.removeEventListener("blur", blur);
    };
  }, []);
  return shift;
}
