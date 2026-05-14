pub mod cli;

mod provider;

pub use provider::GitProvider;
pub use provider::build_partial_patch;
