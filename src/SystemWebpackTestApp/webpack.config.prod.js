/// <binding />
const ExtractTextPlugin = require('extract-text-webpack-plugin');

// Extract compiled CSS into a seperate file
const extractCss = new ExtractTextPlugin({
    filename: 'site.css'
});

module.exports = {
    plugins: [
        extractCss
    ],
    module: {
        rules: [
            {
                test: /\.css$/,
                use: extractCss.extract({
                    use: [
                        {
                            loader: 'css-loader',
                            options: {
                                sourceMap: true,
                            }
                        }
                    ],
                    fallback: 'style-loader'
                })
            }
        ]
    }
};