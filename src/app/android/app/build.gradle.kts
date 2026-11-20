plugins {
    id("com.android.application")
    // The Flutter Gradle Plugin must be applied after the Android and Kotlin Gradle plugins.
    id("dev.flutter.flutter-gradle-plugin")
    id("io.sentry.android.gradle")
}

val uploadKeystoreFile = providers.gradleProperty("murmliUploadKeystoreFile").orNull?.takeIf { it.isNotBlank() }
val uploadKeystorePassword = providers.gradleProperty("murmliUploadKeystorePassword").orNull?.takeIf { it.isNotBlank() }
if ((uploadKeystoreFile == null) != (uploadKeystorePassword == null)) {
    throw GradleException(
        "Set both murmliUploadKeystoreFile and murmliUploadKeystorePassword to sign with the upload key, or neither to sign with the debug key.",
    )
}

android {
    namespace = "de.murmli.app"
    compileSdk = flutter.compileSdkVersion
    ndkVersion = flutter.ndkVersion

    compileOptions {
        sourceCompatibility = JavaVersion.VERSION_17
        targetCompatibility = JavaVersion.VERSION_17
    }

    defaultConfig {
        applicationId = "de.murmli.app"
        // You can update the following values to match your application needs.
        // For more information, see: https://flutter.dev/to/review-gradle-config.
        minSdk = flutter.minSdkVersion
        targetSdk = flutter.targetSdkVersion
        // Uses the version code from pubspec.yaml. When using split APKs, 1000 * ABI_VERSION
        // is added automatically by Flutter. (https://developer.android.com/studio/build/configure-apk-splits#configure-APK-versions)
        // You can force using the value of versionCode by specifying the `-P force-version-code-ignoring-abi=true`
        // flag during build.
        versionCode = flutter.versionCode
        versionName = flutter.versionName
    }

    signingConfigs {
        if (uploadKeystoreFile != null) {
            create("upload") {
                storeFile = file(uploadKeystoreFile)
                storePassword = uploadKeystorePassword
                keyAlias = "upload"
                // keytool's PKCS12 keystores protect the key with the store password.
                keyPassword = uploadKeystorePassword
            }
        }
    }

    buildTypes {
        release {
            // Local release builds without the upload key properties sign with the debug key.
            signingConfig = signingConfigs.findByName("upload") ?: signingConfigs.getByName("debug")
        }
    }

    flavorDimensions += "environment"

    productFlavors {
        create("local") {
            dimension = "environment"
            applicationIdSuffix = ".dev"
            manifestPlaceholders["appLabel"] = "Murmli Dev"
        }
        create("development") {
            dimension = "environment"
            applicationIdSuffix = ".dev"
            manifestPlaceholders["appLabel"] = "Murmli Dev"
        }
        create("production") {
            dimension = "environment"
            manifestPlaceholders["appLabel"] = "Murmli"
        }
    }
}

kotlin {
    compilerOptions {
        jvmTarget = org.jetbrains.kotlin.gradle.dsl.JvmTarget.JVM_17
    }
}

flutter {
    source = "../.."
}

sentry {
    org.set("murmli")
    projectName.set("app")
    authToken.set(providers.environmentVariable("SENTRY_AUTH_TOKEN"))
    // Only CI uploads, on pushes to main; other builds only embed the mapping UUID.
    autoUploadProguardMapping.set(
        providers.gradleProperty("sentryUploadProguardMapping").map { it.toBoolean() }.orElse(false),
    )
    telemetry.set(false)
    // sentry_flutter brings its own Android SDK.
    autoInstallation {
        enabled.set(false)
    }
    // No performance tracing; the instrumentation would also turn logcat calls into breadcrumbs.
    tracingInstrumentation {
        enabled.set(false)
    }
}
