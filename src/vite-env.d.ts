/// <reference types="vite/client" />

interface Window {
	__cheesegit_openRepo?: (path: string) => Promise<string | null>;
}
