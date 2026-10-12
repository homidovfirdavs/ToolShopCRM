import { useCallback, useEffect, useRef, useState } from "react";

interface State<T> {
  key: string;
  data: T | null;
  error: boolean;
}

/**
 * Loads data when `key` changes (or reload() is called).
 * State is set only inside promise callbacks, never synchronously in the effect.
 */
export function useAsyncData<T>(fetcher: () => Promise<T>, key: string) {
  const [tick, setTick] = useState(0);
  const [state, setState] = useState<State<T>>({ key: "", data: null, error: false });
  const fetcherRef = useRef(fetcher);

  useEffect(() => {
    fetcherRef.current = fetcher;
  });

  const fullKey = `${key}#${tick}`;

  useEffect(() => {
    let cancelled = false;
    fetcherRef.current()
      .then((data) => {
        if (!cancelled) setState({ key: fullKey, data, error: false });
      })
      .catch((err) => {
        console.error(err);
        if (!cancelled) setState({ key: fullKey, data: null, error: true });
      });
    return () => {
      cancelled = true;
    };
  }, [fullKey]);

  const reload = useCallback(() => setTick((t) => t + 1), []);
  const loading = state.key !== fullKey;

  return { data: loading ? null : state.data, error: !loading && state.error, loading, reload };
}
