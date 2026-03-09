(function () {
    function parseProblem(response) {
        var fallback = "HTTP " + response.status;
        var contentType = (response.headers.get("content-type") || "").toLowerCase();

        if (contentType.indexOf("application/problem+json") >= 0 || contentType.indexOf("application/json") >= 0) {
            return response.json()
                .then(function (p) {
                    return {
                        detail: (p && (p.detail || p.title || fallback) || fallback).toString(),
                        code: (p && p.code ? p.code : "").toString(),
                        category: (p && p.category ? p.category : "").toString()
                    };
                })
                .catch(function () {
                    return { detail: fallback, code: "", category: "" };
                });
        }

        return response.text()
            .then(function (t) {
                var text = (t || "").trim();
                return { detail: text || fallback, code: "", category: "" };
            })
            .catch(function () {
                return { detail: fallback, code: "", category: "" };
            });
    }

    function initLoginPage() {
        var authStatus = document.getElementById("authStatus");
        if (!authStatus) {
            return;
        }

        function setAuthStatus(message) {
            if (!message) {
                authStatus.classList.remove("visible");
                authStatus.textContent = "";
                return;
            }

            authStatus.textContent = message;
            authStatus.classList.add("visible");
        }

        var googleBtn = document.getElementById("googleSignInBtn");
        if (googleBtn) {
            googleBtn.addEventListener("click", function () {
                window.location.href = "auth/google";
            });
        }

        var microsoftBtn = document.getElementById("microsoftSignInBtn");
        if (microsoftBtn) {
            microsoftBtn.addEventListener("click", function () {
                window.location.href = "auth/microsoft";
            });
        }

        fetch("auth/me", { credentials: "include" })
            .then(function (response) {
                if (response.ok) {
                    location.replace("tokens.html");
                    return null;
                }

                if (response.status === 401) {
                    return null;
                }

                return parseProblem(response).then(function (problem) {
                    setAuthStatus("Unable to verify current session: " + problem.detail);
                    return null;
                });
            })
            .catch(function () {
                setAuthStatus("Unable to verify current session due to a network error.");
            });
    }

    function initTokensPage() {
        function $(id) {
            return document.getElementById(id);
        }

        var statusEl = $("status");
        if (!statusEl) {
            return;
        }

        var csrfToken = "";

        function setStatus(text, kind) {
            var el = $("status");
            if (!el) {
                return;
            }

            el.textContent = text;
            el.classList.remove("status-neutral", "status-ok", "status-warn", "status-bad");
            var cls = kind === "ok"
                ? "status-ok"
                : kind === "bad"
                    ? "status-bad"
                    : kind === "warn"
                        ? "status-warn"
                        : "status-neutral";
            el.classList.add(cls);
        }

        function setBusyState(isBusy) {
            var status = $("status");
            var generate = $("generate");
            if (status) {
                status.setAttribute("aria-busy", isBusy ? "true" : "false");
            }

            if (generate) {
                generate.setAttribute("aria-busy", isBusy ? "true" : "false");
            }
        }

        function updateExamples(email, guid) {
            var appBasePath = new URL(".", window.location.href).pathname.replace(/\/$/, "");
            var base = window.location.origin + appBasePath;
            var pair = email + ":" + guid;
            var responseExample = '{\n' +
                '  "dataset": "CAOP2025",\n' +
                '  "datasetCreatedAtUtc": "2026-03-05T14:27:35.1782843Z",\n' +
                '  "dicofre": "060334",\n' +
                '  "freguesia": "União das freguesias de Coimbra (Sé Nova, Santa Cruz, Almedina e São Bartolomeu)",\n' +
                '  "concelho": "Coimbra",\n' +
                '  "distrito": "Coimbra",\n' +
                '  "areaHa": 833.48,\n' +
                '  "descricao": "Coimbra (Sé Nova, Santa Cruz, Almedina e São Bartolomeu)"\n' +
                '}';

            $("curl").textContent =
                "Linux / macOS\n\n" +
                "curl -X GET \"" + base + "/api/v1/reverse-geocode?lat=40.2033&lon=-8.4103\" \\\n" +
                "  -H \"Authorization: Basic $(printf '" + pair + "' | base64)\"\n\n" +
                "Windows (PowerShell)\n\n" +
                "$pair = \"" + pair + "\"\n" +
                "$base64 = [Convert]::ToBase64String([Text.Encoding]::UTF8.GetBytes($pair))\n\n" +
                "Invoke-RestMethod `\n" +
                "  -Uri \"" + base + "/api/v1/reverse-geocode?lat=40.2033&lon=-8.4103\" `\n" +
                "  -Headers @{ Authorization = \"Basic $base64\" } `\n" +
                "  -Method GET";

            $("respOk").textContent =
                "HTTP/1.1 200 OK\n" +
                "Content-Type: application/json; charset=utf-8\n\n" +
                responseExample;

            $("respErr").textContent =
                "401 Unauthorized\n" +
                "Content-Type: application/problem+json\n" +
                "{\n" +
                "  \"title\": \"Unauthorized\",\n" +
                "  \"status\": 401,\n" +
                "  \"detail\": \"Authorization header is missing or not using Basic authentication.\",\n" +
                "  \"category\": \"platform\",\n" +
                "  \"code\": \"auth_missing_header\",\n" +
                "  \"traceId\": \"00-...\"\n" +
                "}\n\n" +
                "404 Not Found\n" +
                "Content-Type: application/problem+json\n" +
                "{\n" +
                "  \"title\": \"No match found\",\n" +
                "  \"status\": 404,\n" +
                "  \"detail\": \"No Portuguese administrative area was found for the supplied coordinates.\",\n" +
                "  \"category\": \"api\",\n" +
                "  \"code\": \"outside_portugal\",\n" +
                "  \"traceId\": \"00-...\"\n" +
                "}\n\n" +
                "429 Too Many Requests\n" +
                "Content-Type: application/problem+json\n" +
                "{\n" +
                "  \"title\": \"Too many requests\",\n" +
                "  \"status\": 429,\n" +
                "  \"detail\": \"Per-client rate limit exceeded. Please retry later.\",\n" +
                "  \"category\": \"platform\",\n" +
                "  \"code\": \"rate_limit_client_exceeded\",\n" +
                "  \"traceId\": \"00-...\"\n" +
                "}";
        }

        function setTokenUi(email, guid) {
            $("email").value = email || "";
            $("token").value = guid || "";
            $("copyGuid").disabled = !guid;

            if (guid) {
                $("generate").disabled = true;
                updateExamples(email || "<EMAIL>", guid);
                setStatus("Token already exists", "ok");
            } else {
                $("generate").disabled = false;
                $("curl").textContent = "Generate a token first to see examples.";
                $("respOk").textContent = "Generate a token first to see examples.";
                $("respErr").textContent = "Generate a token first to see examples.";
                setStatus("Authenticated (generate a token whenever you need)", "ok");
            }
        }

        function loadMe() {
            return fetch("auth/me", { credentials: "include" }).then(function (r) {
                if (r.status === 200) {
                    return r.json();
                }

                if (r.status === 401) {
                    location.href = "login.html";
                    return null;
                }

                return parseProblem(r).then(function (problem) {
                    var suffix = problem.code ? (" (" + problem.code + ")") : "";
                    throw new Error("Unable to load user profile: " + problem.detail + suffix);
                });
            });
        }

        function getExistingToken() {
            return fetch("auth/client-token", { credentials: "include" }).then(function (r) {
                if (!r.ok) {
                    return null;
                }

                return r.json();
            });
        }

        function loadCsrfToken() {
            return fetch("auth/antiforgery-token", { credentials: "include" }).then(function (r) {
                if (!r.ok) {
                    return parseProblem(r).then(function (problem) {
                        var suffix = problem.code ? (" (" + problem.code + ")") : "";
                        throw new Error(problem.detail + suffix);
                    });
                }

                return r.json().then(function (data) {
                    var token = (data && data.requestToken ? data.requestToken : "").trim();
                    if (!token) {
                        throw new Error("Missing antiforgery token.");
                    }

                    csrfToken = token;
                });
            });
        }

        function generateToken() {
            var preflight = csrfToken ? Promise.resolve() : loadCsrfToken();
            return preflight.then(function () {
                return fetch("auth/client-token", {
                    method: "POST",
                    credentials: "include",
                    headers: {
                        "X-CSRF-TOKEN": csrfToken
                    }
                }).then(function (r) {
                    if (!r.ok) {
                        return parseProblem(r).then(function (problem) {
                            var suffix = problem.code ? (" (" + problem.code + ")") : "";
                            throw new Error(problem.detail + suffix);
                        });
                    }

                    return r.json();
                });
            });
        }

        function doLogout() {
            var preflight = csrfToken ? Promise.resolve() : loadCsrfToken();
            return preflight
                .then(function () {
                    return fetch("logout", {
                        method: "POST",
                        credentials: "include",
                        headers: {
                            "X-CSRF-TOKEN": csrfToken
                        }
                    });
                })
                .then(function (r) {
                    if (!r.ok) {
                        return parseProblem(r).then(function (problem) {
                            var suffix = problem.code ? (" (" + problem.code + ")") : "";
                            throw new Error(problem.detail + suffix);
                        });
                    }

                    location.href = "login.html";
                });
        }

        var logoutBtn = $("logoutBtn");
        if (logoutBtn) {
            logoutBtn.addEventListener("click", function () {
                logoutBtn.disabled = true;
                setStatus("Signing out...", "warn");
                doLogout()
                    .catch(function (e) {
                        var message = e && e.message ? e.message : "Unable to sign out.";
                        setStatus("Logout error: " + message, "bad");
                        logoutBtn.disabled = false;
                    });
            });
        }

        $("generate").addEventListener("click", function () {
            $("generate").disabled = true;
            setBusyState(true);
            setStatus("Generating token...", "warn");

            generateToken()
                .then(function (tok) {
                    var email = tok.email || $("email").value || "";
                    var guid = tok.clientToken || "";

                    $("email").value = email;
                    $("token").value = guid;
                    $("copyGuid").disabled = !guid;

                    if (guid) {
                        updateExamples(email || "<EMAIL>", guid);
                        setStatus("Token generated", "ok");
                        $("generate").disabled = true;
                        setBusyState(false);
                    } else {
                        setStatus("Invalid response", "bad");
                        $("generate").disabled = false;
                        setBusyState(false);
                    }
                })
                .catch(function (e) {
                    setStatus("Error generating token: " + (e && e.message ? e.message : "unknown"), "bad");
                    $("token").value = "";
                    $("copyGuid").disabled = true;
                    $("curl").textContent = "Unable to generate token.";
                    $("respOk").textContent = "";
                    $("respErr").textContent = "";
                    $("generate").disabled = false;
                    setBusyState(false);
                });
        });

        $("token").addEventListener("click", function () {
            $("token").select();
        });

        $("copyGuid").addEventListener("click", function () {
            var guid = ($("token").value || "").trim();
            if (!guid) {
                return;
            }

            navigator.clipboard.writeText(guid)
                .then(function () {
                    setStatus("GUID copied", "ok");
                })
                .catch(function () {
                    setStatus("Unable to copy", "bad");
                });
        });

        setStatus("Loading...", "neutral");
        setBusyState(true);
        loadMe()
            .then(function (me) {
                if (!me) {
                    setBusyState(false);
                    return;
                }

                return loadCsrfToken().then(function () {
                    $("email").value = me.email || "";
                    $("token").value = "";
                    $("copyGuid").disabled = true;

                    return getExistingToken().then(function (existing) {
                        if (existing && existing.hasToken && existing.clientToken) {
                            setTokenUi(existing.email || me.email, existing.clientToken);
                        } else {
                            setTokenUi(me.email, "");
                        }
                        setBusyState(false);
                    });
                });
            })
            .catch(function (e) {
                var message = e && e.message ? e.message : "Unable to initialize credentials page.";
                setStatus("Initialization error: " + message, "bad");
                setBusyState(false);
            });
    }

    function bootstrap() {
        var body = document.body;
        if (!body) {
            return;
        }

        if (body.classList.contains("login-page")) {
            initLoginPage();
            return;
        }

        if (body.classList.contains("tokens-page")) {
            initTokensPage();
        }
    }

    bootstrap();
})();
