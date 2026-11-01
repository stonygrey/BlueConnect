// Copyright © 2026 Stony Grey LLC.
// SPDX-License-Identifier: MIT

pluginManagement {
    repositories {
        google()
        mavenCentral()
        gradlePluginPortal()
    }
}

dependencyResolutionManagement {
    repositoriesMode.set(RepositoriesMode.FAIL_ON_PROJECT_REPOS)
    repositories {
        google()
        mavenCentral()
    }
}

rootProject.name = "zaffre-kotlin"

// The protocol library (pure JVM, tested without a device) and the Android app.
include(":blueconnect")
include(":app")
