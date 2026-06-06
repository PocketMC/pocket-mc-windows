# Mobile Admin Remote Control Plan

This branch intentionally starts with a reviewed implementation plan instead of partial production code.

## Goal

Add a phone-friendly PocketMC admin dashboard that can manage an existing Minecraft server from a browser.

The first public link provider should be Cloudflare Quick Tunnel via cloudflared. The design must keep the provider layer abstract so ngrok and Tailscale can be added later without rewriting the dashboard.