# Imagem da API de pagamento simulada (REQ-19, research R-01). Construída a partir da raiz do
# repositório: docker build -f src/PedidoFulfillment.PagamentosSimulados/Dockerfile .
# Versões fixas, sem "latest", iguais às das imagens da API e do Worker (REQ-07).

FROM mcr.microsoft.com/dotnet/sdk:10.0.403-noble AS compilacao
WORKDIR /fonte

# Restauração numa camada própria, reaproveitada enquanto o projeto não muda.
COPY global.json Directory.Build.props Directory.Packages.props ./
COPY src/PedidoFulfillment.PagamentosSimulados/PedidoFulfillment.PagamentosSimulados.csproj src/PedidoFulfillment.PagamentosSimulados/
RUN dotnet restore src/PedidoFulfillment.PagamentosSimulados/PedidoFulfillment.PagamentosSimulados.csproj

COPY src/PedidoFulfillment.PagamentosSimulados/ src/PedidoFulfillment.PagamentosSimulados/
RUN dotnet publish src/PedidoFulfillment.PagamentosSimulados/PedidoFulfillment.PagamentosSimulados.csproj -c Release --no-restore -o /app

FROM mcr.microsoft.com/dotnet/aspnet:10.0.14-noble AS execucao
WORKDIR /app
COPY --from=compilacao /app .
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
# Usuário sem privilégios que a imagem já traz (app).
USER $APP_UID
ENTRYPOINT ["dotnet", "PedidoFulfillment.PagamentosSimulados.dll"]
