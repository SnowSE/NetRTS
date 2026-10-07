# Database console for the private Azure network (see docs/deploying.md).
# pgweb in sessions mode: you connect from its login form with your Entra ID token as the password.
FROM sosedoff/pgweb:0.17.0
EXPOSE 8081
CMD ["/usr/bin/pgweb", "--bind=0.0.0.0", "--listen=8081", "--sessions"]
