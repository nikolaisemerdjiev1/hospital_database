# Hospital Coordination Platform frontend

This directory contains the React and TypeScript single-page application. It communicates only with the ASP.NET Core API; external services such as RxNorm are always called through that backend.

## Local commands

Use the Node version declared in the repository `.nvmrc`, then run:

```shell
npm ci
npm run dev
```

The application starts at `http://localhost:5173` and expects the API at `http://localhost:5050` by default. Copy `.env.example` to `.env.local` when a different public API URL is needed.

Before opening a pull request, run:

```shell
npm run lint
npm run typecheck
npm run test
npm run build
```

Never place secrets in variables beginning with `VITE_`; those values are compiled into browser assets.
