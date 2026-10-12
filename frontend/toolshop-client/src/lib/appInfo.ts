/** Build label of the running app ("dev" outside a production build, e.g. in unit tests). */
export const APP_BUILD: string = typeof __APP_BUILD__ !== "undefined" ? __APP_BUILD__ : "dev";
