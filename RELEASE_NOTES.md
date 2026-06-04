# BoneFish Release Notes

## v3.3.3 - Experimental features: Notifications & FPS Monitor (optimized for low-end)

Release date: 2026-06-04

Highlights
- Added Windows-native notifications for Roblox (friend online, general notifications)
- Added FPS Monitor Overlay (real-time FPS + frame time)
- Added "Optimize for low-end devices" setting to reduce CPU usage on older hardware

Quick links
- Experimental settings: Settings > Experimental
- Notifications docs: NOTIFICATION_FEATURE.md
- FPS Monitor docs: FPS_MONITOR_FEATURE.md

Low-end optimizations (what changed)
- FPS Monitor UI updates are throttled when "Optimize for low-end devices" is enabled (update interval increases from 1s → 2s)
- Frame counting uses CompositionTarget.Rendering (lightweight) instead of a 1ms timer — reduces CPU wakeups
- Preallocated brushes used for color coding to avoid allocations every update
- Notification polling interval increases from 5s → 15s when low-end optimization is enabled
- Less frequent updates and lighter rendering to keep gameplay smooth on older laptops

How to enable low-end optimizations
1. Open BoneFish
2. Go to Settings → Experimental
3. Enable "Optimalkan untuk perangkat low-end (Rendah)"

Developer notes
- Settings added: `OptimizeForLowEnd` (bool), `EnableFpsMonitor` (bool), `FpsMonitorX` (double), `FpsMonitorY` (double)
- Notification polling delay now respects `OptimizeForLowEnd`
- FPS Monitor update interval respects `OptimizeForLowEnd`

Known limitations & further work
- Overlay measures WPF render frames; it may not perfectly reflect in-game GPU-rendered FPS
- Future improvements: memory/GPU usage stats, performance graph, adaptive quality changes

Changelog (technical)
- Bloxstrap/Integrations/RobloxNotification.cs: notification polling interval adjusted for low-end mode
- Bloxstrap/UI/Elements/FpsMonitorOverlay.xaml.cs: switched to CompositionTarget.Rendering for frame counting, throttled UI updates, reduced allocations
- Bloxstrap/Integrations/FpsMonitorService.cs: initialized based on setting
- Bloxstrap/Models/Persistable/Settings.cs: new settings added
- Bloxstrap/UI/Elements/Settings/Pages/ExperimentalPage.xaml: new toggles added
- Bloxstrap/UI/ViewModels/Settings/ExperimentalViewModel.cs: new properties for binding

Notes for CI-release
- This file (RELEASE_NOTES.md) is CI-friendly and can be displayed as release notes in the release pipeline.
- It includes user-facing highlights and developer technical changes.

---

Enjoy — and let us know feedback for further optimization on ultra-low-end hardware.