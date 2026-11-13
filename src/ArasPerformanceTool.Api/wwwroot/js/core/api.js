export async function getCsrfToken() {
    const response = await fetch("/api/auth/csrf", {
        credentials: "same-origin",
        cache: "no-store"
    });

    if (!response.ok) {
        throw new Error("Failed to get CSRF token.");
    }

    const data = await response.json();

    if (!data?.requestToken) {
        throw new Error("CSRF token is missing.");
    }

    return data.requestToken;
}