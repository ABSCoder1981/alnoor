plugins {
    alias(libs.plugins.android.application)
    alias(libs.plugins.kotlin.compose)
    alias(libs.plugins.kotlin.serialization)
}

android {
    namespace = "com.alnoor.app"
    // Current AndroidX/Compose releases require compiling against API 37.2.
    // targetSdk stays at 36 (PRD section 13); compileSdk does not change runtime behaviour.
    compileSdk {
        version = release(37) {
            minorApiLevel = 2
        }
    }

    defaultConfig {
        applicationId = "com.alnoor.app"
        minSdk = 26
        targetSdk = 36
        versionCode = 1
        versionName = "0.1.0"
    }

    buildTypes {
        debug {
            // Phase 2 test client default: the Android emulator reaches the PC through 10.0.2.2.
            buildConfigField("String", "DEFAULT_DEV_API_BASE_URL", "\"http://10.0.2.2:5080\"")
            // Debug builds let the tester change the server on the login screen.
            buildConfigField("String", "API_BASE_URL", "\"http://10.0.2.2:5080\"")
        }
        release {
            // The Phase 2 test client is not available in release builds.
            buildConfigField("String", "DEFAULT_DEV_API_BASE_URL", "\"\"")
            // Set to https://api.<domain> in Phase 7 (PRD 22). Empty = not configured.
            buildConfigField("String", "API_BASE_URL", "\"\"")
            isMinifyEnabled = true
            proguardFiles(getDefaultProguardFile("proguard-android-optimize.txt"), "proguard-rules.pro")
            // Release signing uses a private keystore kept outside Git (configured in Phase 7).
        }
    }

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    buildFeatures {
        compose = true
        buildConfig = true
    }
}

dependencies {
    implementation(libs.androidx.core.ktx)
    implementation(libs.androidx.activity.compose)
    implementation(libs.androidx.lifecycle.runtime.compose)
    implementation(libs.androidx.lifecycle.viewmodel.compose)
    implementation(platform(libs.androidx.compose.bom))
    implementation(libs.androidx.compose.ui)
    implementation(libs.androidx.compose.ui.tooling.preview)
    implementation(libs.androidx.compose.material3)
    debugImplementation(libs.androidx.compose.ui.tooling)

    implementation(libs.livekit.android)
    implementation(libs.livekit.android.compose)
    implementation(libs.retrofit)
    implementation(libs.retrofit.kotlinx.serialization)
    implementation(libs.kotlinx.serialization.json)
    implementation(libs.androidx.datastore.preferences)
    implementation(libs.androidx.navigation.compose)
    implementation(libs.tink.android)

    testImplementation(libs.junit)
    testImplementation(libs.okhttp.mockwebserver)
    testImplementation(libs.kotlinx.coroutines.test)
}
