# R8 rewrites the monitor/catch-all structure of Java-WebSocket's thread classes in a way ART's verifier
# rejects (VerifyError at runtime, only in release builds). Keep the library untouched.
-keep class org.java_websocket.** { *; }
-dontwarn org.slf4j.**

# QR scanning (ZXing) is reached through an Activity declared in its own manifest.
-keep class com.google.zxing.** { *; }
-keep class com.journeyapps.barcodescanner.** { *; }
