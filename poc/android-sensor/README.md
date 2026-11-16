# Android light sensor experiment

Foreground-only proof of concept. It reads the physical Android `TYPE_LIGHT` sensor via `SensorManager` and displays lux, Android sensor event timestamp (nanoseconds since boot), reported accuracy and event count. It explicitly reports an unavailable sensor and unregisters when the activity pauses. It does not send data, run a foreground service, or establish screen-off/Doze support. Sensor values are not simulated.

From the repository root, first run `cd poc/android-sensor`, then run `flutter test` and `flutter analyze`. Build with `flutter build apk --debug` after installing Android SDK command-line tools and accepting licenses. On a physical Android device, run the app, record device model, OS version, sensor name where available, values, accuracy and event counts. Cover/uncover the sensor and observe on-change behavior. Minimize, lock the screen, enable battery saving and resume; this experiment intentionally pauses its listener in the background, so it does not test a foreground service. Record observations; do not infer sustained sensing from a resumed reading. Repeat on another model. A separate service PoC is required for background, Doze, networking and reconnection.

## Emulator observation (2026-10-28)

On an Android 35 x86_64 Google APIs AVD, the debug APK launched and displayed a simulated light sensor event. Setting the emulator sensor to 50 lux changed the display from 0.0 to 50.0 and increased the event count from 1 to 2. These are virtual readings, not physical-sensor or background evidence. The build succeeded with Android Studio's bundled JDK; the GraalVM 25 JDK failed in Android's jlink transform. The app is still a foreground-only experiment.
