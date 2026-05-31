import { describe, it, expect, vi, beforeEach } from "vitest";
import { render, screen, fireEvent, waitFor } from "@testing-library/react";
import { AddRemoteDialog } from "./AddRemoteDialog";

const mockAddRemote = vi.fn();
const mockListRemotes = vi.fn();

vi.mock("../../../ipc/bindings", () => ({
  commands: {
    addRemote: (...args: unknown[]) => mockAddRemote(...args),
    listRemotes: (...args: unknown[]) => mockListRemotes(...args),
    sshAddKey: vi.fn().mockResolvedValue({ status: "ok", data: null }),
  },
}));

vi.mock("../../../shared/stores/alerts", () => ({
  useAlertStore: Object.assign(
    (selector: (s: Record<string, unknown>) => unknown) => selector({ addAlert: vi.fn() }),
    { getState: () => ({ addAlert: vi.fn() }), subscribe: () => () => {} },
  ),
}));

describe("AddRemoteDialog", () => {
  const defaultProps = {
    repoPath: "/repo",
    existingRemotes: [],
    onClose: vi.fn(),
    onAdded: vi.fn(),
  };

  beforeEach(() => {
    vi.clearAllMocks();
    mockAddRemote.mockResolvedValue({ status: "ok", data: null });
  });

  it("renders the dialog with URL and name fields", () => {
    render(<AddRemoteDialog {...defaultProps} />);
    expect(screen.getByTestId("add-remote-dialog")).toBeInTheDocument();
    expect(screen.getByTestId("add-remote-url")).toBeInTheDocument();
    expect(screen.getByTestId("add-remote-name")).toBeInTheDocument();
  });

  it("pre-fills 'origin' as the name when no remotes exist", () => {
    render(<AddRemoteDialog {...defaultProps} existingRemotes={[]} />);
    const nameInput = screen.getByTestId("add-remote-name") as HTMLInputElement;
    expect(nameInput.value).toBe("origin");
  });

  it("does not pre-fill name when remotes already exist", () => {
    render(
      <AddRemoteDialog
        {...defaultProps}
        existingRemotes={[{ name: "origin", url: "https://example.com/repo.git" }]}
      />,
    );
    const nameInput = screen.getByTestId("add-remote-name") as HTMLInputElement;
    expect(nameInput.value).toBe("");
  });

  it("shows error when remote name already exists", () => {
    render(
      <AddRemoteDialog
        {...defaultProps}
        existingRemotes={[{ name: "origin", url: "https://example.com/repo.git" }]}
      />,
    );
    const nameInput = screen.getByTestId("add-remote-name");
    fireEvent.change(nameInput, { target: { value: "origin" } });
    expect(screen.getByTestId("add-remote-name-error")).toBeInTheDocument();
    expect(screen.getByText(/already exists/)).toBeInTheDocument();
  });

  it("disables the confirm button when fields are empty", () => {
    render(<AddRemoteDialog {...defaultProps} existingRemotes={[{ name: "origin", url: "https://example.com" }]} />);
    // Name is empty by default when remotes exist
    const confirmButton = screen.getByTestId("add-remote-confirm");
    expect(confirmButton).toBeDisabled();
  });

  it("calls addRemote when confirm is clicked with valid inputs", async () => {
    render(<AddRemoteDialog {...defaultProps} />);
    const urlInput = screen.getByTestId("add-remote-url");
    fireEvent.change(urlInput, { target: { value: "https://github.com/user/repo.git" } });
    // Name is already "origin" for first remote

    const confirmButton = screen.getByTestId("add-remote-confirm");
    fireEvent.click(confirmButton);

    await waitFor(() => {
      expect(mockAddRemote).toHaveBeenCalledWith("/repo", "origin", "https://github.com/user/repo.git");
    });
  });

  it("calls onAdded after successful addition", async () => {
    const onAdded = vi.fn();
    render(<AddRemoteDialog {...defaultProps} onAdded={onAdded} />);
    const urlInput = screen.getByTestId("add-remote-url");
    fireEvent.change(urlInput, { target: { value: "https://github.com/user/repo.git" } });

    const confirmButton = screen.getByTestId("add-remote-confirm");
    fireEvent.click(confirmButton);

    await waitFor(() => {
      expect(onAdded).toHaveBeenCalled();
    });
  });

  it("closes on cancel click", () => {
    const onClose = vi.fn();
    render(<AddRemoteDialog {...defaultProps} onClose={onClose} />);
    const cancelButton = screen.getByTestId("add-remote-cancel");
    fireEvent.click(cancelButton);
    expect(onClose).toHaveBeenCalled();
  });

  it("auto-suggests remote name from SSH URL", () => {
    render(
      <AddRemoteDialog
        {...defaultProps}
        existingRemotes={[{ name: "origin", url: "https://example.com/repo.git" }]}
      />,
    );
    const urlInput = screen.getByTestId("add-remote-url");
    fireEvent.change(urlInput, { target: { value: "ssh://git@codeberg.org/user/repo.git" } });

    const nameInput = screen.getByTestId("add-remote-name") as HTMLInputElement;
    expect(nameInput.value).toBe("codeberg");
  });

  it("auto-suggests remote name from git@ URL", () => {
    render(
      <AddRemoteDialog
        {...defaultProps}
        existingRemotes={[{ name: "origin", url: "https://example.com/repo.git" }]}
      />,
    );
    const urlInput = screen.getByTestId("add-remote-url");
    fireEvent.change(urlInput, { target: { value: "git@github.com:user/repo.git" } });

    const nameInput = screen.getByTestId("add-remote-name") as HTMLInputElement;
    expect(nameInput.value).toBe("github");
  });

  it("appends number if auto-suggested name is taken", () => {
    render(
      <AddRemoteDialog
        {...defaultProps}
        existingRemotes={[
          { name: "origin", url: "https://example.com/repo.git" },
          { name: "github", url: "https://github.com/old/repo.git" },
        ]}
      />,
    );
    const urlInput = screen.getByTestId("add-remote-url");
    fireEvent.change(urlInput, { target: { value: "git@github.com:user/repo.git" } });

    const nameInput = screen.getByTestId("add-remote-name") as HTMLInputElement;
    expect(nameInput.value).toBe("github2");
  });

  it("validates remote name characters", () => {
    render(<AddRemoteDialog {...defaultProps} />);
    const nameInput = screen.getByTestId("add-remote-name");
    fireEvent.change(nameInput, { target: { value: "invalid name!" } });
    expect(screen.getByTestId("add-remote-name-error")).toBeInTheDocument();
  });
});
