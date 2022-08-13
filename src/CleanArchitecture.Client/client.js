import Cookies from "js-cookie";
import { BehaviorSubject } from "rxjs";
import QueryString from "qs";
import Axios from "axios";
import https from "https";

const createCookieStorage = (origin) => {

    const cookieURL = new URL(origin);
    const cookieDomain = cookieURL.hostname;
    const cookieSecure = cookieURL.protocol === 'https';

    return {
        get: (key) => {
            const itemStr = Cookies.get(key);
            // if the item doesn't exist, return null
            if (!itemStr) {
                return null;
            }

            const item = JSON.parse(itemStr);
            return item.value;
        },
        set: (key, value, ttl) => {
            const now = new Date();

            const item = {
                value
            };

            Cookies.set(key, JSON.stringify(item), {
                expires: new Date(now.getTime() + ttl),
                domain: cookieDomain,
                secure: cookieSecure,
            });
        },
        remove: (key) => {
            Cookies.remove(key);
        }
    };
};

const getStore = (origin) => {
    const accessTokenStorageKey = `_jwt_access_token_storage_${process.env.NODE_ENV}`;
    const refreshTokenStorageKey = `_jwt_refresh_token_storage_${process.env.NODE_ENV}`;
    const userStorageKey = `_jwt_user_storage_${process.env.NODE_ENV}`;

    const storage = createCookieStorage(origin);
    const userSubject = new BehaviorSubject(storage.get(userStorageKey));

    const getAccessToken = () => {
        return storage.get(accessTokenStorageKey);
    };

    const setAccessToken = (value, ttl) => {
        storage.set(accessTokenStorageKey, value, ttl);
    };

    const removeAccessToken = () => {
        storage.remove(accessTokenStorageKey);
    };

    const getRefreshToken = () => {
        return storage.get(refreshTokenStorageKey);
    };

    const setRefreshToken = (value, ttl) => {
        storage.set(refreshTokenStorageKey, value, ttl);
    };

    const removeRefreshToken = () => {
        storage.remove(refreshTokenStorageKey);
    };

    const getUser = () => {
        return userSubject.getValue();
    };

    const setUser = (value, ttl) => {
        storage.set(userStorageKey, value, ttl);
        userSubject.next(value);
    };

    const removeUser = () => {
        storage.remove(userStorageKey);
        userSubject.next(null);
    };

    const clear = () => {
        removeAccessToken();
        removeRefreshToken();
        removeUser();
    };

    return {
        getAccessToken,
        setAccessToken,
        removeAccessToken,

        getRefreshToken,
        setRefreshToken,
        removeRefreshToken,

        getUser,
        setUser,
        removeUser,

        userSubject,
        clear
    };
};

const getErrorState = (config) => {
    const namespace = 'axios-jwt-error';
    const errorState = config[namespace] || {};
    errorState.retry = errorState.retry || false;
    errorState.queued = errorState.queued || false;
    config[namespace] = errorState;
    return errorState;
};

const axiosJwt = (axios, settings) => {

    const authHeaderKey = "Authorization";
    const authHeaderPrefix = "Bearer ";

    const defaultSettings = {
        unauthorized: (response) => {
            return response.status === 401;
        }
    };

    settings = Object.assign({}, defaultSettings, settings || {});

    const store = getStore(settings.clientURL);

    let refreshing = false;
    let queue = [];

    // Function that resolves all items in the queue with the provided token
    const resolveQueue = () => {
        queue.forEach((p) => {
            p.resolve();
        });

        queue = [];
    };

    // Function that declines all items in the queue with the provided error
    const declineQueue = () => {
        queue.forEach((p) => {
            p.reject();
        });

        queue = [];
    };

    // Add a request interceptor
    axios.interceptors.request.use((config) => {
        const accessToken = store.getAccessToken();
        const refreshToken = store.getRefreshToken();
        const user = store.getUser();

        if (accessToken && refreshToken && user) {
            config.headers[authHeaderKey] = `${authHeaderPrefix}${accessToken}`;
        }
        else {
            delete config.headers[authHeaderKey];
            delete axios.defaults.headers.common[authHeaderKey];
        }

        return config;
    }, (error) => Promise.reject(error));

    // Add a response interceptor
    axios.interceptors.response.use((response) => response,
        (error) => {

            if (error.response) {

                if (settings.unauthorized(error.response)) {

                    // If we have no configraution to retry the request
                    const { config } = error;
                    if (!config) return Promise.reject(error);

                    // If we have already retired the request
                    const errorState = getErrorState(config);
                    if (errorState.retry || errorState.queued) return Promise.reject(error);

                    if (refreshing) {
                        return new Promise((resolve, reject) => queue.push({ resolve, reject }))
                            .then(() => {
                                errorState.queued = true;
                                return axios.request(config);
                            }).catch(() => {
                                return Promise.reject(error); // Ignore refresh token request's error and return actual "error" for the original request
                            });
                    }
                    else {

                        const refreshToken = store.getRefreshToken();

                        if (!refreshToken) {
                            store.clear();
                            return Promise.reject(error);
                        }

                        errorState.retry = true;
                        refreshing = true;

                        const refreshTokenCallback = settings.refreshTokenCallback(axios, { refreshToken })
                            .then((response) => {
                                const { accessToken, accessTokenExpiresIn, refreshToken, refreshTokenExpiresIn, user } = response.data;
                                store.setAccessToken(accessToken, accessTokenExpiresIn);
                                store.setRefreshToken(refreshToken, refreshTokenExpiresIn);
                                store.setUser(user, refreshTokenExpiresIn);
                            })
                            .catch((error) => {

                                if (error.response) {
                                    if (settings.unauthorized(error.response))
                                        store.clear();
                                }

                                return Promise.reject(error);
                            })
                            .finally(() => {
                                refreshing = false;
                            });

                        return refreshTokenCallback.then(() => {
                            resolveQueue();
                            return axios.request(config);
                        }).catch(() => {
                            declineQueue();
                            return Promise.reject(error);
                        });
                    }
                }

                // The request was made and the server responded with a status code
                // that falls out of the range of 2xx
                return Promise.reject(error);
            } else if (error.request) {

                // The request was made but no response was received
                // `error.request` is an instance of XMLHttpRequest in the browser and an instance of
                // http.ClientRequest in node.js
                return Promise.reject(error);
            } else {

                // Something happened in setting up the request that triggered an Error
                return Promise.reject(error);
            }
        });

    return {
        ...axios,
        store,

        user: store.userSubject,
        signin: (data, requestConfig) => {
            return settings.generateTokenCallback(axios, data, requestConfig)
                .then((response) => {
                    const { accessToken, accessTokenExpiresIn, refreshToken, refreshTokenExpiresIn, user } = response.data;
                    store.setAccessToken(accessToken, accessTokenExpiresIn);
                    store.setRefreshToken(refreshToken, refreshTokenExpiresIn);
                    store.setUser(user, refreshTokenExpiresIn);
                    return response;
                });
        },

        signout: () => {
            const refreshToken = store.getRefreshToken();

            if (!refreshToken) {
                store.clear();
                return Promise.resolve();
            }

            return settings.revokeTokenCallback(axios, { refreshToken })
                .then((response) => {
                    store.clear();
                    return response;
                })
                .catch((error) => {
                    store.clear();
                    console.log(error);
                });
        }
    }
};

const SERVER_URL = typeof window != 'undefined' ? window.env.SERVER_URL : process.env.SERVER_URL;
const CLIENT_URL = typeof window != 'undefined' ? window.env.SERVER_URL : process.env.SERVER_URL;
const ENV_MODE = typeof window != 'undefined' ? window.env.ENV_MODE : process.env.NODE_ENV;

const client = axiosJwt(Axios.create({
    baseURL: SERVER_URL,
    paramsSerializer: params => {
        return QueryString.stringify(params)
    },
    httpsAgent: (() => {

        if (ENV_MODE === 'development') {
            const httpsAgent = new https.Agent({
                rejectUnauthorized: false,
            });
            // eslint-disable-next-line no-console
            console.log(ENV_MODE `RejectUnauthorized is disabled.`);
            return httpsAgent;
        }

        return undefined;
    })()
}),
    {
        clientURL: CLIENT_URL,
        generateTokenCallback: (request, data, requestConfig) => request.post('account/token/generate', data, requestConfig),
        refreshTokenCallback: (request, data, requestConfig) => request.post('account/token/refresh', data, requestConfig),
        revokeTokenCallback: (request, data, requestConfig) => request.post('account/token/revoke', data, requestConfig)
    });

export default client;