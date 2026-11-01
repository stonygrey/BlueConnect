// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT
//
// The protocol library: segments, frames, the Noise link over Torann, and the
// Verdigris commands. A plain JVM module so that its tests run without a
// device or an emulator. The Android backend is in :app.

plugins {
    alias(libs.plugins.kotlin.jvm)
}

java {
    sourceCompatibility = JavaVersion.VERSION_17
    targetCompatibility = JavaVersion.VERSION_17
}

kotlin {
    compilerOptions {
        jvmTarget.set(org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17)
    }
}

sourceSets {
    main {
        // lib/gen-proto writes the protobuf code here from proto/verdigris.proto.
        java.srcDir("src/generated/java")
        kotlin.srcDir("src/generated/kotlin")
    }
}

dependencies {
    api(libs.torann)
    api(libs.protobuf.kotlin.lite)
    api(libs.coroutines.core)
    testImplementation(libs.junit)
    testImplementation(libs.coroutines.test)
}

tasks.test {
    useJUnit()
    testLogging {
        events("passed", "failed", "skipped")
    }
}
