# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Status

Greenfield repository. As of the initial commit it contains only `README.md`, `LICENSE`, and a .NET `.gitignore` — no source code, build scripts, or project files yet. The sections below are placeholders to fill in once code lands; do not treat them as describing existing structure.

## Intended purpose

The repository name (`wpf-rust-compare-charts`) plus the .NET-oriented `.gitignore` indicate the goal is to **compare charting implementations between a WPF (C#/.NET) app and a Rust app** — likely benchmarking rendering performance and/or developer experience of the same charts in both stacks.

Confirm this intent with the user before scaffolding either side.

## Architecture

_Not yet established._ Once the WPF and Rust projects exist, document here:
- Layout of the WPF/.NET solution (projects, charting library used).
- Layout of the Rust crate(s) (charting/GUI crate used).
- What the two sides share (sample datasets, scenarios, measurement methodology) and how results are compared.

## Commands

_Not yet established._ Fill in once project files exist. Expected shapes:
- **.NET / WPF:** `dotnet build`, `dotnet run --project <proj>`, `dotnet test`, single test via `dotnet test --filter <FullyQualifiedName>`.
- **Rust:** `cargo build`, `cargo run`, `cargo test`, single test via `cargo test <name>`.

Replace these with the actual project paths and any benchmarking commands when they are added.
