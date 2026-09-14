pipeline {
    agent none

    options {
        disableConcurrentBuilds()
        disableResume()
        disableRestartFromStage()
    }

    stages {
        stage('Integration Tests') {
            steps {
                script {
                    def properties = readTrusted('.jenkins/pesterProject.properties')
                    def pesterConfig = readProperties text: properties

                    pesterProject(pesterConfig)
                }
            }
        }
    }
}

def requiredProperty(config, name) {
    def value = config[name]?.trim()
    if (!value) {
        error "Missing required property '${name}' in .jenkins/pesterProject.properties"
    }
    return value
}

def commaSeparated(value) {
    return value
        ? value.split(' ').collect { it.trim() }.findAll { it }
        : []
}

def parseCredentialPairs(value, description, bindingFactory) {
    return commaSeparated(value).collect { entry ->
        def parts = entry.split('\\|', 2)
        if (parts.size() != 2 || !parts[0].trim() || !parts[1].trim()) {
            error "Invalid ${description} credential binding '${entry}'; expected variable|credential-id"
        }
        return bindingFactory(parts[0].trim(), parts[1].trim())
    }
}

def credentialBindings(config) {
    def bindings = []
    bindings.addAll(parseCredentialPairs(
        config.usernamePasswordCredentials,
        'username/password',
        { variable, id ->
            usernamePassword(
                credentialsId: id,
                usernameVariable: "${variable}_USR",
                passwordVariable: "${variable}_PSW"
            )
        }
    ))
    bindings.addAll(parseCredentialPairs(
        config.stringCredentials,
        'string',
        { variable, id -> string(credentialsId: id, variable: variable) }
    ))
    return bindings
}

def withOptionalCredentials(bindings, Closure body) {
    if (bindings) {
        withCredentials(bindings, body)
    } else {
        body()
    }
}

def pesterProject(config) {
    def targets = commaSeparated(requiredProperty(config, 'targets'))
    def variants = commaSeparated(requiredProperty(config, 'variants'))
    def timeoutMinutes = (config.timeoutMinutes?.trim() ?: '60') as Integer
    def bindings = credentialBindings(config)

    if (timeoutMinutes <= 0) {
        error 'timeoutMinutes must be a positive integer'
    }

    for (def target in targets) {
        stage("Host: ${target}") {
            node(target) {
                def os = isWindows() ? 'windows' : 'linux'

                checkout scm

                dir('.reports') {
                    deleteDir()
                }

                withEnvFile {
                    for (def variant in variants) {
                        def safeTarget = target.replaceAll('[^A-Za-z0-9_.-]+', '-')
                        def safeVariant = variant.replaceAll('[^A-Za-z0-9_.-]+', '-')
                        def resultsPath = ".reports/pester-${safeTarget}-${os}-${safeVariant}.xml"

                        def image = "${env.DOCKER_NAMESPACE}/${env.DOCKER_IMAGE}:${variant}"

                        withOptionalCredentials(bindings) {
                            stage(image) {
                                catchError(
                                    message: "Pester integration tests failed for ${image} on ${target}",
                                    stageResult: 'FAILURE',
                                    buildResult: 'FAILURE',
                                    catchInterruptions: false
                                ) {
                                    timeout(time: timeoutMinutes, unit: 'MINUTES') {
                                        echo "Testing ${image} on ${target}"
                                        try {
                                            exec "pwsh -NoLogo -NoProfile -NonInteractive -File .jenkins/Invoke-IntegrationTests.ps1 -Variant ${variant} -Pull -TestsPath tests -ResultsPath ${resultsPath}"
                                        } finally {
                                            junit(
                                                testResults: resultsPath,
                                                allowEmptyResults: false,
                                                skipMarkingBuildUnstable: false
                                            )
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
        }
    }
}
