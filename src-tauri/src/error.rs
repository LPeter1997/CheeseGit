use serde::Serialize;
use specta::Type;
use thiserror::Error;

/// Centralized error type for the application.
#[derive(Debug, Error, Serialize, Type)]
pub enum AppError {
    #[error("Git error: {0}")]
    Git(String),

    #[error("IO error: {0}")]
    Io(String),

    #[error("SSH authentication required: {0}")]
    SshAuthRequired(String),

    #[error("{0}")]
    Other(String),
}
