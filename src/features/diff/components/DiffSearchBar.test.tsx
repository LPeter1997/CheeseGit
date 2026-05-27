import { describe, it, expect, vi } from "vitest";
import { render, screen, fireEvent } from "@testing-library/react";
import { DiffSearchBar } from "./DiffSearchBar";

describe("DiffSearchBar", () => {
  const defaultProps = {
    query: "test",
    onQueryChange: vi.fn(),
    currentIndex: 0,
    totalMatches: 5,
    onNext: vi.fn(),
    onPrevious: vi.fn(),
    isSearching: false,
    focusSignal: 0,
  };

  it("renders the search input", () => {
    render(<DiffSearchBar {...defaultProps} />);

    const input = screen.getByPlaceholderText("Search diff…");
    expect(input).toBeInTheDocument();
    expect((input as HTMLInputElement).value).toBe("test");
  });

  it("displays match counter", () => {
    render(<DiffSearchBar {...defaultProps} currentIndex={2} totalMatches={5} />);

    expect(screen.getByText("3 of 5")).toBeInTheDocument();
  });

  it("displays 'No matches' when totalMatches is 0", () => {
    render(<DiffSearchBar {...defaultProps} totalMatches={0} />);

    expect(screen.getByText("No matches")).toBeInTheDocument();
  });

  it("calls onQueryChange when text is entered", async () => {
    render(<DiffSearchBar {...defaultProps} />);

    const input = screen.getByPlaceholderText("Search diff…") as HTMLInputElement;
    fireEvent.change(input, { target: { value: "new search" } });

    expect(defaultProps.onQueryChange).toHaveBeenCalledWith("new search");
  });

  it("calls onNext when next button is clicked", () => {
    render(<DiffSearchBar {...defaultProps} totalMatches={5} />);

    const nextButton = screen.getByTitle("Next match (Return)");
    fireEvent.click(nextButton);

    expect(defaultProps.onNext).toHaveBeenCalled();
  });

  it("calls onPrevious when previous button is clicked", () => {
    render(<DiffSearchBar {...defaultProps} totalMatches={5} />);

    const prevButton = screen.getByTitle("Previous match (Shift+Return)");
    fireEvent.click(prevButton);

    expect(defaultProps.onPrevious).toHaveBeenCalled();
  });

  it("keeps navigation buttons visible when there are no matches", () => {
    render(<DiffSearchBar {...defaultProps} totalMatches={0} />);

    expect(screen.getByTitle("Next match (Return)")).toBeInTheDocument();
    expect(screen.getByTitle("Previous match (Shift+Return)")).toBeInTheDocument();
  });

  it("shows 'Searching...' when isSearching is true", () => {
    render(<DiffSearchBar {...defaultProps} isSearching={true} />);

    expect(screen.getByText("Searching…")).toBeInTheDocument();
  });

  it("disables navigation buttons when searching", () => {
    render(<DiffSearchBar {...defaultProps} totalMatches={5} isSearching={true} />);

    expect(screen.getByTitle("Next match (Return)")).toBeDisabled();
    expect(screen.getByTitle("Previous match (Shift+Return)")).toBeDisabled();
  });

  it("clears query when Escape is pressed", () => {
    render(<DiffSearchBar {...defaultProps} />);

    const input = screen.getByPlaceholderText("Search diff…") as HTMLInputElement;
    fireEvent.keyDown(input, { key: "Escape" });

    expect(defaultProps.onQueryChange).toHaveBeenCalledWith("");
  });

  it("auto-focuses the input on mount", () => {
    render(<DiffSearchBar {...defaultProps} />);

    const input = screen.getByPlaceholderText("Search diff…");
    expect(document.activeElement).toBe(input);
  });

  it("re-focuses input when focusSignal changes", () => {
    const { rerender } = render(<DiffSearchBar {...defaultProps} focusSignal={0} />);

    const input = screen.getByPlaceholderText("Search diff…") as HTMLInputElement;
    input.blur();
    expect(document.activeElement).not.toBe(input);

    rerender(<DiffSearchBar {...defaultProps} focusSignal={1} />);
    expect(document.activeElement).toBe(input);
  });
});
